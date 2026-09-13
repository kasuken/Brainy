using Brainy.Application.DTOs.Calendar;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages the per-user credential that authenticates the read-only ICS calendar feed
/// (issue #314). The methods here that read/write "the current user's" token depend on
/// <c>ICurrentUserService</c> and are for the authenticated account-settings UI;
/// <see cref="ResolveUserIdAsync"/> is the opposite shape — it takes a raw token exactly
/// as presented on a public, anonymous URL and resolves the owning user, which is how the
/// feed endpoint itself authenticates a request instead of relying on a login cookie.
/// </summary>
public interface ICalendarFeedTokenService
{
    /// <summary>Returns the current user's feed token status (never the raw token itself).</summary>
    Task<CalendarFeedTokenStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new random token for the current user (replacing any existing one, which
    /// stops working immediately) and returns its raw value. This is the only place the raw
    /// value is ever available; it cannot be retrieved again later.
    /// </summary>
    Task<CalendarFeedRegeneratedDto> RegenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the current user's feed token. Any calendar client still polling the old
    /// subscription URL starts getting 404s on its very next request.
    /// </summary>
    Task RevokeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a raw token presented on the public feed URL and returns the owning user's
    /// id, or <c>null</c> if the token is unknown, revoked, or malformed. Never throws for an
    /// invalid token — an invalid token is an expected, common case (a stale bookmark, a
    /// just-revoked subscription), not an error. On success, best-effort records the access
    /// time for the account-settings UI.
    /// </summary>
    Task<string?> ResolveUserIdAsync(string? rawToken, CancellationToken cancellationToken = default);
}
