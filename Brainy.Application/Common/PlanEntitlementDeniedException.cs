namespace Brainy.Application.Common;

/// <summary>
/// Thrown when a server-side entitlement check blocks a paid-plan-gated action (an
/// active-project cap, AI availability, or AI allowance). Carries a user-facing explanation
/// so callers can surface a clear message instead of a generic error.
/// </summary>
public sealed class PlanEntitlementDeniedException(string message) : InvalidOperationException(message);

/// <summary>
/// Thrown when an edit is attempted on a project flipped to read-only by
/// <c>IEntitlementService.ReconcileProjectAccessAsync</c> (over the active-project limit
/// after a downgrade). The project remains fully viewable and exportable; archiving it or
/// upgrading the plan is the only way to make it editable again.
/// </summary>
public sealed class ProjectReadOnlyException(string projectName)
    : InvalidOperationException(
        $"'{projectName}' is read-only because it exceeds your plan's active-project limit. " +
        "Archive it or upgrade your plan to edit it again.");
