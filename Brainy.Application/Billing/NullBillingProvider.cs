using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Billing;

/// <summary>
/// Safe no-op <see cref="IBillingProvider"/> used when <c>Billing:Provider</c> is
/// <c>None</c> (the default). Checkout and portal requests report themselves as unsupported
/// with an honest reason instead of faking a working payment flow; webhook signatures always
/// fail verification, so no unauthenticated payload can apply a plan change. Plan changes
/// happen only through the internal/admin path
/// (<see cref="Interfaces.Services.IEntitlementService.SetPlanTierAsync"/>) until a real
/// provider is configured.
/// </summary>
/// <remarks>
/// <see cref="StripeBillingProvider"/> is the live drop-in replacement, selected by
/// <c>Billing:Provider = Stripe</c> in <c>DependencyInjection.AddBilling</c>. Nothing else in
/// the entitlement system changes between the two.
/// </remarks>
internal sealed class NullBillingProvider : IBillingProvider
{
    private const string NotConfiguredReason =
        "No payment provider is configured yet. Contact us to upgrade.";

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        PlanTier targetTier,
        BillingInterval interval = BillingInterval.Yearly,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckoutSessionResult(false, null, NotConfiguredReason));

    public Task<PortalSessionResult> CreatePortalSessionAsync(
        string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PortalSessionResult(false, null, NotConfiguredReason));

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload, string signatureHeader, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookVerificationResult(false, "No payment provider is configured; webhooks are not accepted."));

    public Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(
        string payload, CancellationToken cancellationToken = default) =>
        Task.FromResult<ParsedBillingWebhookEvent?>(null);
}
