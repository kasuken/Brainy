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

    /// <summary>
    /// The provider's price id for the Pro plan's recurring subscription (e.g. a Stripe
    /// <c>price_...</c> id for the "$2/month, billed yearly" price). Required when
    /// <see cref="Provider"/> is <see cref="BillingProviderType.Stripe"/>.
    /// </summary>
    public string? ProPriceId { get; set; }

    /// <summary>
    /// The absolute base URL of this Brainy deployment (e.g. <c>https://app.example.com</c>, no
    /// trailing slash), used to build the checkout success/cancel and billing-portal return
    /// URLs the provider redirects back to. Required when <see cref="Provider"/> is
    /// <see cref="BillingProviderType.Stripe"/>.
    /// </summary>
    public string? AppBaseUrl { get; set; }
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
    /// Stripe hosted Checkout and Customer Portal, via the official Stripe.net SDK. Requires
    /// <see cref="BillingOptions.ApiKey"/>, <see cref="BillingOptions.WebhookSigningSecret"/>,
    /// <see cref="BillingOptions.ProPriceId"/>, and <see cref="BillingOptions.AppBaseUrl"/>.
    /// </summary>
    Stripe,
}
