namespace Brainy.Application.DTOs.Billing;

/// <summary>Snapshot of a user's hosted-AI allowance, for both enforcement and display.</summary>
/// <param name="AiAvailableOnPlan">Whether the current plan includes hosted AI at all.</param>
/// <param name="HasAllowanceRemaining">Whether at least one more hosted AI request may be made right now.</param>
/// <param name="Limit">Requests allowed per period. Null means unlimited.</param>
/// <param name="Used">Requests consumed in the current period.</param>
/// <param name="PeriodResetsAtUtc">When the current period's usage counter resets.</param>
/// <param name="BringYourOwnKeyAllowed">Whether this plan allows bypassing hosted metering with a user-supplied key.</param>
public sealed record AiAllowanceStatus(
    bool AiAvailableOnPlan,
    bool HasAllowanceRemaining,
    int? Limit,
    int Used,
    DateTime? PeriodResetsAtUtc,
    bool BringYourOwnKeyAllowed);
