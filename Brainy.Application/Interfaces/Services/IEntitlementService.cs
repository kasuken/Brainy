using Brainy.Application.DTOs.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Enforces Brainy's plan matrix (<see cref="Billing.PlanCatalog"/>) server-side. Every
/// paid-only action must go through here rather than being gated only in a Razor component
/// (issue #296 acceptance criterion: "No paid-only action is controlled only by client-side UI").
/// </summary>
public interface IEntitlementService
{
    /// <summary>Whether the current user may create another active project right now.</summary>
    Task<EntitlementCheckResult> CanCreateProjectAsync(CancellationToken cancellationToken = default);

    /// <summary>The current user's hosted-AI allowance, for display and pre-flight checks.</summary>
    Task<AiAllowanceStatus> GetAiAllowanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically checks and consumes one unit of the current user's hosted-AI allowance.
    /// Call this immediately before invoking <c>IAiAssistant</c>; on a denied result, do not
    /// call the assistant.
    /// </summary>
    Task<EntitlementCheckResult> TryConsumeAiAllowanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Everything the Plan &amp; usage screen needs for the current user.</summary>
    Task<PlanUsageSummaryDto> GetUsageSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-applies the active-project cap for <paramref name="userId"/>: the oldest-created
    /// non-archived projects beyond the current plan's limit are flipped read-only, and any
    /// project no longer over the limit (e.g. after an upgrade, or after archiving others)
    /// is flipped back to writable. Call after any plan-tier change and after archiving,
    /// restoring, or deleting a project.
    /// </summary>
    Task ReconcileProjectAccessAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <paramref name="userId"/>'s plan tier, optionally records the new renewal
    /// timestamp, reconciles project read-only state for the new tier, and records
    /// <c>AnalyticsEvents.PlanConverted</c>. Not scoped to "current user": called from the
    /// internal/admin plan-change path and from the billing webhook processor, neither of
    /// which necessarily runs in the affected user's own HTTP context.
    /// </summary>
    Task SetPlanTierAsync(
        string userId,
        PlanTier tier,
        DateTime? planRenewsAtUtc = null,
        CancellationToken cancellationToken = default);
}
