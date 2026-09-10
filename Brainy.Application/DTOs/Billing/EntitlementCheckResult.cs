namespace Brainy.Application.DTOs.Billing;

/// <summary>
/// The outcome of an entitlement check: a typed, user-renderable result rather than an
/// exception, so a UI can show the consequence of a limit before the user is blocked
/// (issue #296 acceptance criterion). Service layers that enforce this server-side (e.g.
/// <c>ProjectService.CreateAsync</c>) still throw when a denied result is bypassed.
/// </summary>
public sealed record EntitlementCheckResult(bool IsAllowed, string? Reason = null)
{
    public static EntitlementCheckResult Allow() => new(true);

    public static EntitlementCheckResult Deny(string reason) => new(false, reason);
}
