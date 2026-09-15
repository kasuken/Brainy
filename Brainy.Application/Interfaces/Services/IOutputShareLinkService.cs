using Brainy.Application.DTOs.Outputs;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages the per-output credential that authenticates the anonymous, read-only public share
/// page for one <see cref="Brainy.Domain.Entities.Output"/> (issue #319). The owner-facing
/// methods here depend on <c>ICurrentUserService</c> and scope every read/write to the current
/// user's own output; <see cref="ResolvePublicAsync"/> is the opposite shape — it takes a raw
/// token exactly as presented on a public, anonymous URL and resolves the shared content,
/// which is how the public share page authenticates a request instead of relying on a login.
/// </summary>
public interface IOutputShareLinkService
{
    /// <summary>
    /// Returns the given output's share-link status (never the raw token itself), or
    /// <c>null</c> if the output does not exist or is not owned by the current user.
    /// </summary>
    Task<OutputShareLinkStatusDto?> GetStatusAsync(Guid outputId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new random share link for the current user's output (replacing any existing
    /// one, which stops working immediately) and returns its raw value. This is the only place
    /// the raw value is ever available; it cannot be retrieved again later. Throws
    /// <see cref="KeyNotFoundException"/> if the output does not exist or is not owned by the
    /// current user.
    /// </summary>
    Task<OutputShareLinkCreatedDto> EnableAsync(
        Guid outputId,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the current user's share link for the given output, if one exists. Anyone still
    /// holding the old public URL starts getting 404s on their very next request. Never throws
    /// for an output with no active link — revoking an already-revoked or never-created link
    /// is an idempotent no-op.
    /// </summary>
    Task RevokeAsync(Guid outputId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a raw token presented on the public share URL and returns the shared output's
    /// own content, or <c>null</c> if the token is unknown, revoked, expired, or malformed.
    /// Never throws for an invalid token — an invalid token is an expected, common case (a
    /// stale bookmark, a just-revoked link), not an error, and must always look identical to
    /// "no such page" to an anonymous caller. On success, best-effort records the access time.
    /// </summary>
    Task<SharedOutputDto?> ResolvePublicAsync(string? rawToken, CancellationToken cancellationToken = default);
}
