using System.Security.Cryptography;
using System.Text;
using Brainy.Application.DTOs.Outputs;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IOutputShareLinkService"/>. Only a SHA-256 hash of the token is ever
/// persisted (see <see cref="OutputShareLink"/>) — a database leak does not hand over a live
/// share link, the same treatment Brainy gives any other credential (compare
/// <see cref="CalendarFeedTokenService"/>, whose pattern this mirrors). Exactly one row exists
/// per output; enabling again overwrites it in place rather than accumulating history.
/// </summary>
internal sealed class OutputShareLinkService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IOutputShareLinkService
{
    // 256 bits of entropy, hex-encoded — long enough that guessing is infeasible, and plain
    // hex keeps the token trivially URL-safe without any percent-encoding edge cases.
    private const int TokenBytes = 32;

    public async Task<OutputShareLinkStatusDto?> GetStatusAsync(Guid outputId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var isOwned = await context.Outputs.AsNoTracking()
            .AnyAsync(o => o.Id == outputId && o.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!isOwned)
            return null;

        var link = await context.OutputShareLinks.AsNoTracking()
            .Where(l => l.OutputId == outputId && l.UserId == userId)
            .Select(l => new { l.CreatedAtUtc, l.ExpiresAtUtc, l.RevokedAtUtc, l.LastAccessedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (link is null)
            return new OutputShareLinkStatusDto(false, null, null, null, null);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var isActive = link.RevokedAtUtc is null && (link.ExpiresAtUtc is null || link.ExpiresAtUtc > now);

        return new OutputShareLinkStatusDto(
            isActive,
            link.CreatedAtUtc,
            link.ExpiresAtUtc,
            link.RevokedAtUtc,
            link.LastAccessedAtUtc);
    }

    public async Task<OutputShareLinkCreatedDto> EnableAsync(
        Guid outputId,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var outputOwned = await context.Outputs.AsNoTracking()
            .AnyAsync(o => o.Id == outputId && o.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!outputOwned)
            throw new KeyNotFoundException($"Output '{outputId}' was not found.");

        if (expiresAtUtc is not null && expiresAtUtc <= now)
            throw new ArgumentException("Expiry must be in the future.", nameof(expiresAtUtc));

        var rawToken = GenerateRawToken();
        var hash = Hash(rawToken);

        var existing = await context.OutputShareLinks
            .FirstOrDefaultAsync(l => l.OutputId == outputId && l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.OutputShareLinks.Add(new OutputShareLink
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OutputId = outputId,
                TokenHash = hash,
                ExpiresAtUtc = expiresAtUtc,
                RevokedAtUtc = null,
                LastAccessedAtUtc = null
            });
        }
        else
        {
            // Overwriting the hash immediately invalidates the previous raw token: it no
            // longer hashes to anything in the table, so it stops resolving before this call
            // even returns.
            existing.TokenHash = hash;
            existing.ExpiresAtUtc = expiresAtUtc;
            existing.RevokedAtUtc = null;
            existing.LastAccessedAtUtc = null;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OutputShareLinkCreatedDto(rawToken, now, expiresAtUtc);
    }

    public async Task RevokeAsync(Guid outputId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await context.OutputShareLinks
            .FirstOrDefaultAsync(l => l.OutputId == outputId && l.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null || existing.RevokedAtUtc is not null)
            return;

        existing.RevokedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedOutputDto?> ResolvePublicAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !IsPlausibleToken(rawToken))
            return null;

        var hash = Hash(rawToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var link = await context.OutputShareLinks
            .FirstOrDefaultAsync(l => l.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        // A revoked or expired link must be indistinguishable from an unknown one: never
        // reveal via a different response shape that a token once existed (see #319's
        // guardrail that a revoked/expired link returns a clean 404, never a 403).
        if (link is null || link.RevokedAtUtc is not null || (link.ExpiresAtUtc is not null && link.ExpiresAtUtc <= now))
            return null;

        var output = await context.Outputs.AsNoTracking()
            .Where(o => o.Id == link.OutputId)
            .Select(o => new { o.Title, o.Description, o.Content })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The output itself may have been deleted (cascade removes the link too, but guard
        // defensively against any ordering surprise) — treat it the same as an unknown token.
        if (output is null)
            return null;

        link.LastAccessedAtUtc = now;
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Recording "last accessed" is a courtesy for the owner's share-status UI, not a
            // correctness requirement: a lost race against a concurrent revoke/regenerate
            // must never turn into a failed public view.
        }

        return new SharedOutputDto(output.Title, output.Description, output.Content);
    }

    private static bool IsPlausibleToken(string rawToken) =>
        rawToken.Length == TokenBytes * 2 && rawToken.All(Uri.IsHexDigit);

    private static string GenerateRawToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
