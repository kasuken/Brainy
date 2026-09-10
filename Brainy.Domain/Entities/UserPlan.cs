using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// Per-user billing/subscription state: current plan tier, renewal/trial/grace-period
/// timestamps, the billing provider's customer/subscription ids (populated only when a real
/// <c>IBillingProvider</c> is configured), and the current AI-allowance usage counter.
/// One record per user; created lazily on first access, defaulting to <see cref="PlanTier.Starter"/>
/// (mirrors <see cref="UserDashboardPreference"/>'s "no row yet = default" pattern).
/// </summary>
/// <remarks>
/// Deliberately a standalone entity keyed by <see cref="UserId"/> rather than fields on
/// <c>ApplicationUser</c>: <c>Brainy.Application</c> (where the entitlement service must live)
/// cannot reference <c>Brainy.Data.Identity.ApplicationUser</c> without creating a circular
/// project reference, since <c>Brainy.Data</c> already references <c>Brainy.Application</c>.
/// </remarks>
public class UserPlan : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    public PlanTier Tier { get; set; } = PlanTier.Starter;

    /// <summary>When the current billing period renews. Null until a real billing provider populates it.</summary>
    public DateTime? PlanRenewsAtUtc { get; set; }

    /// <summary>When an active trial ends. Null when the user is not in a trial.</summary>
    public DateTime? TrialEndsAtUtc { get; set; }

    /// <summary>
    /// When a payment-failure grace period ends. While set and in the future, the user keeps
    /// paid access despite a failed charge; once it elapses, a reconciliation should downgrade the plan.
    /// </summary>
    public DateTime? GracePeriodEndsAtUtc { get; set; }

    /// <summary>The billing provider's customer id. Null under <c>NullBillingProvider</c>.</summary>
    public string? BillingProviderCustomerId { get; set; }

    /// <summary>The billing provider's subscription id. Null under <c>NullBillingProvider</c>.</summary>
    public string? BillingProviderSubscriptionId { get; set; }

    /// <summary>Hosted AI requests consumed in the current allowance period.</summary>
    public int AiAllowanceUsedInPeriod { get; set; }

    /// <summary>Start of the current AI-allowance period; null means no request has been made yet.</summary>
    public DateTime? AiAllowancePeriodStartUtc { get; set; }
}
