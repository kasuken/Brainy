namespace Brainy.Domain.Enums;

/// <summary>
/// The billing plan a user is currently on. See <c>Brainy.Application.Billing.PlanCatalog</c>
/// for the enforced entitlement matrix behind each tier (issue #296).
/// </summary>
public enum PlanTier
{
    /// <summary>Free tier: unlimited capture, PARA organization, up to 3 active projects, no AI.</summary>
    Starter,

    /// <summary>Paid tier: unlimited projects/areas/goals, AI features, Outputs, priority support.</summary>
    Pro,
}
