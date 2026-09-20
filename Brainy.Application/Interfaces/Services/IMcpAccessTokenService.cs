using Brainy.Application.DTOs.Mcp;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages the per-user bearer credentials that authenticate Model Context Protocol (MCP)
/// clients (issue: MCP server, phase 1). The methods that read/write "the current user's"
/// tokens depend on <c>ICurrentUserService</c> and back the authenticated token-management UI;
/// <see cref="ResolveUserIdAsync"/> is the opposite shape — it takes a raw token exactly as
/// presented in an <c>Authorization: Bearer</c> header on the MCP endpoint and resolves the
/// owning user, which is how the endpoint authenticates a request instead of relying on a
/// login cookie. This mirrors <see cref="ICalendarFeedTokenService"/>, except a user may hold
/// several MCP tokens at once (one per client), so tokens are issued and revoked individually.
/// </summary>
public interface IMcpAccessTokenService
{
    /// <summary>Returns the current user's MCP tokens (never the raw values), newest first.</summary>
    Task<IReadOnlyList<McpAccessTokenDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a new random token for the current user, labelled <paramref name="name"/>, and
    /// returns its raw value. This is the only place the raw value is ever available; it
    /// cannot be retrieved again later. Throws <see cref="ArgumentException"/> for a blank or
    /// over-long name, and <see cref="InvalidOperationException"/> once the per-user active
    /// token limit is reached (revoke an unused token first).
    /// </summary>
    Task<McpAccessTokenSecretDto> IssueAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes one of the current user's tokens by id. Any MCP client still using it starts
    /// getting 401s on its very next request. Returns <c>false</c> if no active token with
    /// that id belongs to the current user (already revoked, unknown, or another user's).
    /// </summary>
    Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a raw token presented in the MCP endpoint's <c>Authorization</c> header and
    /// returns the owning user's id, or <c>null</c> if the token is unknown, revoked, or
    /// malformed. Never throws for an invalid token — an invalid token is an expected case
    /// (a stale client config, a just-revoked credential), not an error. On success,
    /// best-effort records the access time for the token-management UI.
    /// </summary>
    Task<string?> ResolveUserIdAsync(string? rawToken, CancellationToken cancellationToken = default);
}
