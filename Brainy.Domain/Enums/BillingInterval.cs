namespace Brainy.Domain.Enums;

/// <summary>
/// The billing cadence a paid plan is purchased on. Brainy markets Pro as "$2 / month,
/// billed yearly" and offers monthly billing at checkout (see the <c>/pricing</c> page),
/// so a checkout flow must be able to say which of the two the user picked.
/// </summary>
public enum BillingInterval
{
    /// <summary>Charged every month.</summary>
    Monthly,

    /// <summary>Charged once a year. Brainy's marketed default.</summary>
    Yearly,
}
