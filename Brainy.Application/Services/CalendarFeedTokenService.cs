using System.Security.Cryptography;
using System.Text;
using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="ICalendarFeedTokenService"/>. Only a SHA-256 hash of the token is
/// ever persisted (see <see cref="CalendarFeedToken"/>) — a database leak does not hand over
/// a live feed, the same treatment Brainy gives any other credential. Exactly one row exists
/// per user; regenerating overwrites it in place rather than accumulating history.
/// </summary>
internal sealed class CalendarFeedTokenService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : ICalendarFeedTokenService
{
    // 256 bits of entropy, hex-encoded — long enough that guessing is infeasible, and plain
    // hex keeps the token trivially URL-safe without any percent-encoding edge cases.
    private const int TokenBytes = 32;

    public async Task<CalendarFeedTokenStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var token = await context.CalendarFeedTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.CreatedAtUtc, t.LastAccessedAtUtc, t.RevokedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return token is null
            ? new CalendarFeedTokenStatusDto(false, null, null, null)
            : new CalendarFeedTokenStatusDto(
                token.RevokedAtUtc is null,
                token.CreatedAtUtc,
                token.LastAccessedAtUtc,
                token.RevokedAtUtc);
    }

    public async Task<CalendarFeedRegeneratedDto> RegenerateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var rawToken = GenerateRawToken();
        var hash = Hash(rawToken);

        var existing = await context.CalendarFeedTokens
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.CalendarFeedTokens.Add(new CalendarFeedToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = hash,
                RevokedAtUtc = null,
                LastAccessedAtUtc = null
            });
        }
        else
        {
            // Overwriting the hash immediately invalidates the previous raw token: it no
            // longer hashes to anything in the table, so it stops authenticating requests
            // before this call even returns.
            existing.TokenHash = hash;
            existing.RevokedAtUtc = null;
            existing.LastAccessedAtUtc = null;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CalendarFeedRegeneratedDto(rawToken, now);
    }

    public async Task RevokeAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await context.CalendarFeedTokens
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null || existing.RevokedAtUtc is not null)
            return;

        existing.RevokedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ResolveUserIdAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !IsPlausibleToken(rawToken))
            return null;

        var hash = Hash(rawToken);

        var token = await context.CalendarFeedTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
            return null;

        token.LastAccessedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Recording "last accessed" is a courtesy for the settings UI, not a
            // correctness requirement: a lost race against a concurrent poll (or a
            // regenerate/revoke landing at the same instant) must never turn into a
            // failed feed request.
        }

        return token.UserId;
    }

    private static bool IsPlausibleToken(string rawToken) =>
        rawToken.Length == TokenBytes * 2 && rawToken.All(Uri.IsHexDigit);

    private static string GenerateRawToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
