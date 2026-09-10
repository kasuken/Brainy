using Brainy.Domain.Enums;

namespace Brainy.Application.Billing;

/// <summary>
/// One row of Brainy's plan matrix: every marketed capability (see the <c>/pricing</c> page)
/// mapped to a value <see cref="Services.EntitlementService"/> actually enforces. This is
/// the single written source of truth required by issue #296 — "a written plan matrix maps
/// every marketed capability to an enforced entitlement" — and is also rendered to users on
/// the Plan &amp; usage screen (see <c>Manage.razor</c>), so it is not just a code comment.
/// </summary>
/// <param name="Tier">The plan tier this row describes.</param>
/// <param name="DisplayName">User-facing plan name.</param>
/// <param name="PriceDescription">User-facing price, matching the <c>/pricing</c> page copy.</param>
/// <param name="MaxActiveProjects">
/// Maximum non-archived projects. Null means unlimited. Enforced in
/// <c>IEntitlementService.CanCreateProjectAsync</c> and <c>ReconcileProjectAccessAsync</c>.
/// </param>
/// <param name="AiAvailable">
/// Whether hosted AI features (summaries, action-item extraction, PARA suggestions, output
/// drafting, duplicate detection) are available at all on this tier. False for Starter:
/// the <c>/pricing</c> page lists "AI suggestions with provenance" only under Pro, so Starter
/// getting a small trial allowance would contradict already-published marketing copy.
/// </param>
/// <param name="AiAllowancePerPeriod">
/// Hosted AI requests allowed per <see cref="AiAllowancePeriod"/>. Null means unlimited.
/// Whether hosted AI should be metered at all is an explicit open question in issue #296
/// ("Decision needed: ... whether hosted AI is metered"); until that is decided, Pro defaults
/// to unlimited (null) rather than a fabricated numeric cap, so this file does not silently
/// assert an unmade business decision. Set a finite value here once that decision ships.
/// </param>
/// <param name="AiAllowancePeriod">The rolling window <see cref="AiAllowancePerPeriod"/> resets on.</param>
/// <param name="AllowsBringYourOwnKey">
/// Whether a user may supply their own AI provider key to bypass hosted metering entirely.
/// True on every tier: BYOK cannot cost Brainy hosted-AI spend, so it is a safe default that
/// does not contradict any published pricing claim. This flag is the entitlement-matrix
/// declaration only — actual per-user key storage/UI is not implemented in this change;
/// see the remarks on <see cref="Interfaces.Billing.IBillingProvider"/>.
/// </param>
/// <param name="Capabilities">User-facing bullet list, matching the <c>/pricing</c> page.</param>
public sealed record PlanDefinition(
    PlanTier Tier,
    string DisplayName,
    string PriceDescription,
    int? MaxActiveProjects,
    bool AiAvailable,
    int? AiAllowancePerPeriod,
    TimeSpan AiAllowancePeriod,
    bool AllowsBringYourOwnKey,
    IReadOnlyList<string> Capabilities);

/// <summary>The plan matrix: every <see cref="PlanTier"/> mapped to its enforced entitlements.</summary>
public static class PlanCatalog
{
    public static readonly PlanDefinition Starter = new(
        Tier: PlanTier.Starter,
        DisplayName: "Starter",
        PriceDescription: "$0 / forever",
        MaxActiveProjects: 3,
        AiAvailable: false,
        AiAllowancePerPeriod: 0,
        AiAllowancePeriod: TimeSpan.FromDays(30),
        AllowsBringYourOwnKey: true,
        Capabilities:
        [
            "Unlimited notes and capture",
            "PARA organization and tags",
            "Inbox processing",
            "Up to 3 active projects",
            "Today view and tasks",
            "Search",
        ]);

    public static readonly PlanDefinition Pro = new(
        Tier: PlanTier.Pro,
        DisplayName: "Pro",
        PriceDescription: "$2 / month, billed yearly",
        MaxActiveProjects: null,
        AiAvailable: true,
        AiAllowancePerPeriod: null,
        AiAllowancePeriod: TimeSpan.FromDays(30),
        AllowsBringYourOwnKey: true,
        Capabilities:
        [
            "Everything in Starter",
            "Unlimited projects, areas, and goals",
            "AI suggestions with provenance",
            "Summaries and action-item extraction",
            "Outputs",
            "Calendar and planning views",
            "Priority support",
        ]);

    /// <summary>All plan definitions, in display order.</summary>
    public static readonly IReadOnlyList<PlanDefinition> All = [Starter, Pro];

    public static PlanDefinition Get(PlanTier tier) => tier switch
    {
        PlanTier.Starter => Starter,
        PlanTier.Pro => Pro,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown plan tier."),
    };
}
