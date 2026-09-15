using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A per-user credential that authenticates the read-only ICS calendar feed
/// (<c>GET /api/calendar/feed.ics</c>) for calendar clients that cannot perform an
/// interactive login. Exactly one row exists per user (enforced by a unique index on
/// <see cref="UserId"/>); regenerating replaces <see cref="TokenHash"/> in place and
/// revoking sets <see cref="RevokedAtUtc"/> without deleting the row, so the audit trail
/// (when the feed was first enabled, when it was last used) survives both operations.
/// </summary>
/// <remarks>
/// Only a salted SHA-256 hash of the token is ever persisted — never the raw value —
/// so a database leak does not hand over a live feed, the same treatment as a password
/// or API key. The raw token is generated and shown to the user exactly once, at
/// creation/regeneration time, and is never logged or included in telemetry.
/// </remarks>
public class CalendarFeedToken : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 hash of the raw token. The raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Set when the user revokes the feed; a non-null value means the token no longer
    /// authenticates any request, immediately and without waiting for expiry.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Updated on every successful feed request; surfaced in the account settings UI.</summary>
    public DateTime? LastAccessedAtUtc { get; set; }
}
