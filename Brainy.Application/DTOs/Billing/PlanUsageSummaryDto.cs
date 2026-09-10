using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Billing;

/// <summary>
/// Everything the "Plan &amp; usage" screen needs: current plan, consumption against each
/// limit, reset timing, and the AI/provider implications — shown before a user is blocked
/// (issue #296 acceptance criterion).
/// </summary>
public sealed record PlanUsageSummaryDto(
    PlanTier Tier,
    string PlanDisplayName,
    string PlanPriceDescription,
    int ActiveProjectCount,
    int? MaxActiveProjects,
    bool ProjectLimitReached,
    int ReadOnlyProjectCount,
    AiAllowanceStatus AiAllowance,
    DateTime? PlanRenewsAtUtc,
    DateTime? TrialEndsAtUtc,
    DateTime? GracePeriodEndsAtUtc);
