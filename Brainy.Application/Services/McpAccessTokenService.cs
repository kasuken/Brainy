using System.Security.Cryptography;
using System.Text;
using Brainy.Application.DTOs.Mcp;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IMcpAccessTokenService"/>. Only a SHA-256 hash of the token is ever
/// persisted (see <see cref="McpAccessToken"/>) — a database leak does not hand over live MCP
/// access, the same treatment Brainy gives any other credential. Unlike the single-row
/// calendar feed token, a user may hold several MCP tokens at once (one per client), each
/// issued and revoked individually.
/// </summary>
internal sealed class McpAccessTokenService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IMcpAccessTokenService
{
    // 256 bits of entropy, hex-encoded — long enough that guessing is infeasible.
    private const int TokenBytes = 32;

    // A recognizable, greppable prefix on the raw token so a leaked credential can be caught
    // by secret scanners and is obviously a Brainy MCP token in a client's configuration. The
    // full raw string (prefix included) is what gets hashed and compared, so the prefix costs
    // nothing at lookup time.
    private const string TokenPrefix = "brainy_mcp_";

    // A modest ceiling per user: enough for every plausible client (desktop, mobile, editor,
    // a spare) while stopping unbounded issuance. Revoked tokens do not count — they are kept
    // only for their audit trail.
    private const int MaxActiveTokensPerUser = 20;

    private const int MaxNameLength = 100;

    public async Task<IReadOnlyList<McpAccessTokenDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.McpAccessTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new McpAccessTokenDto(
                t.Id,
                t.Name,
                t.RevokedAtUtc == null,
                t.CreatedAtUtc,
                t.LastUsedAtUtc,
                t.RevokedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<McpAccessTokenSecretDto> IssueAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Token name must be {MaxNameLength} characters or fewer.", nameof(name));
        }

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var activeCount = await context.McpAccessTokens
            .CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);
        if (activeCount >= MaxActiveTokensPerUser)
        {
            throw new InvalidOperationException(
                $"You already have the maximum of {MaxActiveTokensPerUser} active MCP tokens. " +
                "Revoke one you no longer use before issuing another.");
        }

        var rawToken = GenerateRawToken();
        var id = Guid.NewGuid();

        context.McpAccessTokens.Add(new McpAccessToken
        {
            Id = id,
            UserId = userId,
            Name = trimmedName,
            TokenHash = Hash(rawToken),
            RevokedAtUtc = null,
            LastUsedAtUtc = null
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new McpAccessTokenSecretDto(id, trimmedName, rawToken, now);
    }

    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Scope the lookup to the current user so a token id belonging to someone else can
        // never be revoked, and an already-revoked token is a no-op that reports false.
        var token = await context.McpAccessTokens
            .FirstOrDefaultAsync(
                t => t.Id == tokenId && t.UserId == userId && t.RevokedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
            return false;

        token.RevokedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<string?> ResolveUserIdAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !IsPlausibleToken(rawToken))
            return null;

        var hash = Hash(rawToken);

        var token = await context.McpAccessTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
            return null;

        token.LastUsedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Recording "last used" is a courtesy for the management UI, not a correctness
            // requirement: a lost race against a concurrent request (or a revoke landing at
            // the same instant) must never turn into a failed MCP request.
        }

        return token.UserId;
    }

    private static bool IsPlausibleToken(string rawToken)
    {
        if (rawToken.Length != TokenPrefix.Length + (TokenBytes * 2) ||
            !rawToken.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = TokenPrefix.Length; i < rawToken.Length; i++)
        {
            if (!Uri.IsHexDigit(rawToken[i]))
                return false;
        }

        return true;
    }

    private static string GenerateRawToken() =>
        TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenBytes));

    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
