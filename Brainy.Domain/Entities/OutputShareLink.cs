using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A per-output, opt-in credential that authenticates an anonymous, read-only public view of
/// exactly one <see cref="Output"/> (issue #319). Exactly one row exists per output (enforced
/// by a unique index on <see cref="OutputId"/>); enabling/regenerating replaces
/// <see cref="TokenHash"/> in place and revoking sets <see cref="RevokedAtUtc"/> without
/// deleting the row, mirroring <see cref="CalendarFeedToken"/> — the same proven pattern,
/// scaled from "one per user" to "one per output".
/// </summary>
/// <remarks>
/// Only a SHA-256 hash of the token is ever persisted — never the raw value — so a database
/// leak does not hand over a live share link. The raw token is generated and shown to the
/// owning user exactly once, when the link is enabled or regenerated, and is never logged or
/// included in telemetry. This is deliberately the narrowest possible sharing primitive: one
/// output, read-only, no recipient, no permissions model, no notion of collaboration.
/// </remarks>
public class OutputShareLink : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user (the output's owner).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The single output this link exposes, read-only, to anonymous visitors.</summary>
    public Guid OutputId { get; set; }

    /// <summary>Hex-encoded SHA-256 hash of the raw token. The raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Optional expiry set by the owner at creation time. A public request made after this
    /// instant is treated identically to a revoked or unknown token: a clean 404.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Set when the owner revokes the link; a non-null value means the token no longer
    /// resolves for any request, immediately and without waiting for expiry.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Updated on every successful public view; surfaced next to the share indicator.</summary>
    public DateTime? LastAccessedAtUtc { get; set; }
}
