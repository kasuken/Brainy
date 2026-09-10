namespace Brainy.Application.Options;

/// <summary>Configuration options for the billing/payment provider.</summary>
public sealed class BillingOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "Billing";

    /// <summary>The billing provider to use. Defaults to <see cref="BillingProviderType.None"/> (no live payments).</summary>
    public BillingProviderType Provider { get; set; } = BillingProviderType.None;

    /// <summary>API key/secret for the chosen provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Shared secret used to verify inbound webhook signatures.</summary>
    public string? WebhookSigningSecret { get; set; }
}

/// <summary>Supported billing provider back-ends.</summary>
public enum BillingProviderType
{
    /// <summary>
    /// No live payment integration. Plan changes only happen through the internal/admin path
    /// (<c>IEntitlementService.SetPlanTierAsync</c>) or a manually-applied webhook test event.
    /// This is Brainy's only working option today — there is no live Stripe/Paddle account to
    /// integrate with yet (see issue #296's "Decision needed" section).
    /// </summary>
    None,

    /// <summary>
    /// Reserved for a future Stripe integration. Not implemented: selecting this throws at
    /// startup so configuration cannot silently claim a working payment flow that isn't there.
    /// </summary>
    Stripe,
}
