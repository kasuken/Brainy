namespace Brainy.Application.Interfaces.Identity;

/// <summary>
/// Lets a background job (no live request, circuit, or <c>HttpContext</c> to resolve a user
/// from) impersonate one user for the lifetime of a single DI scope, so scoped services that
/// depend on <see cref="ICurrentUserService"/> — such as the Today aggregation used by push
/// notification dispatch (issue #315) — can be reused unmodified instead of duplicating their
/// query logic with an explicit-userId variant.
/// </summary>
/// <remarks>
/// Implemented in the Web layer as a plain scoped holder that <c>CurrentUserService</c>
/// consults before falling back to the circuit/HttpContext principal. A caller must create a
/// fresh DI scope per user, set <see cref="UserId"/> once at the start of that scope, and
/// never share the scope (or this instance) across users or requests.
/// </remarks>
public interface IBackgroundUserContextAccessor
{
    /// <summary>The impersonated user id for this scope, or null to defer to the normal request/circuit resolution.</summary>
    string? UserId { get; set; }
}
