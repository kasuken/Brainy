using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A per-user bearer credential that authenticates a Model Context Protocol (MCP) client
/// (Claude, Copilot, ChatGPT, or a local MCP host) against Brainy's <c>/api/mcp</c> endpoint.
/// MCP clients connect over plain HTTP with no interactive login, so the token presented in
/// the <c>Authorization</c> header is the entire credential — the same shape of problem the
/// read-only ICS calendar feed already solves with <see cref="CalendarFeedToken"/>.
/// </summary>
/// <remarks>
/// Deliberately unlike <see cref="CalendarFeedToken"/> in one respect: a user may hold
/// <b>several</b> MCP tokens at once (one per client, each with its own <see cref="Name"/>),
/// so there is no unique index on <see cref="UserId"/> — only on <see cref="TokenHash"/>.
/// Revoking one client's token must not disturb the others, so revocation targets a single
/// row by id and sets <see cref="RevokedAtUtc"/> without deleting it, preserving the audit
/// trail (when it was issued, when it was last used).
///
/// Only a salted-free SHA-256 hash of the token is ever persisted — never the raw value —
/// so a database leak does not hand over live access, the same treatment Brainy gives a
/// password or the calendar feed token. The raw token is generated and shown to the user
/// exactly once, at issue time, and is never logged or included in telemetry.
/// </remarks>
public class McpAccessToken : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User-supplied label identifying which client this token was issued for
    /// (for example "Claude Desktop" or "VS Code Copilot"), shown in the token-management UI
    /// so a specific client's access can be recognized and revoked.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 hash of the raw token. The raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Set when the user revokes this token; a non-null value means it no longer
    /// authenticates any request, immediately and without waiting for expiry.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Updated on every successful MCP request; surfaced in the token-management UI.</summary>
    public DateTime? LastUsedAtUtc { get; set; }
}
