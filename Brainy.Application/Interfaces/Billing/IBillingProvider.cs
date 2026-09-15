using Brainy.Application.DTOs.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Billing;

/// <summary>
/// Provider-agnostic billing/payment abstraction, mirroring how <c>IAiAssistant</c> abstracts
/// the AI provider: a <c>Billing:Provider</c> config value selects the implementation,
/// <see cref="Billing.NullBillingProvider"/> is the safe no-op default when none is configured,
/// and <c>StripeBillingProvider</c> is the live implementation. Adding another provider
/// (Paddle, etc.) means implementing this interface and registering it in
/// <c>DependencyInjection.AddBilling</c>; nothing in the entitlement system changes.
/// </summary>
public interface IBillingProvider
{
    /// <summary>
    /// Starts a hosted checkout flow for upgrading <paramref name="userId"/> to
    /// <paramref name="targetTier"/> on <paramref name="interval"/>. Defaults to
    /// <see cref="BillingInterval.Yearly"/>, the cadence the <c>/pricing</c> page headlines.
    /// </summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        PlanTier targetTier,
        BillingInterval interval = BillingInterval.Yearly,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a hosted self-service billing-portal session for <paramref name="userId"/>.</summary>
    Task<PortalSessionResult> CreatePortalSessionAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Verifies an inbound webhook request's signature before its payload is trusted.</summary>
    Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload, string signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a signature-verified webhook payload into the fields Brainy's entitlement
    /// model needs. Returns null for event types Brainy does not act on.
    /// </summary>
    Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(string payload, CancellationToken cancellationToken = default);
}
