using Brainy.Application.DTOs.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Billing;

/// <summary>
/// Provider-agnostic billing/payment abstraction, mirroring how <c>IAiAssistant</c> abstracts
/// the AI provider: a <c>Billing:Provider</c> config value selects the implementation, and
/// <see cref="Billing.NullBillingProvider"/> is the safe no-op default when none is configured.
/// A real integration (Stripe, Paddle, etc.) is a drop-in replacement — implement this
/// interface and register it in <c>DependencyInjection.AddBilling</c>.
/// </summary>
public interface IBillingProvider
{
    /// <summary>Starts a hosted checkout flow for upgrading <paramref name="userId"/> to <paramref name="targetTier"/>.</summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, PlanTier targetTier, CancellationToken cancellationToken = default);

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
