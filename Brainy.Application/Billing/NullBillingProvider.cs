using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Billing;

/// <summary>
/// Safe no-op <see cref="IBillingProvider"/> used when <c>Billing:Provider</c> is
/// <c>None</c> (the default, and Brainy's only configured option today — there is no live
/// Stripe/Paddle account in this environment). Checkout and portal requests report
/// themselves as unsupported with an honest reason instead of faking a working payment
/// flow; webhook signatures always fail verification, so no unauthenticated payload can
/// apply a plan change. Plan changes happen only through the internal/admin path
/// (<see cref="Interfaces.Services.IEntitlementService.SetPlanTierAsync"/>) until a real
/// provider is configured.
/// </summary>
/// <remarks>
/// A real Stripe/Paddle/etc. implementation is a drop-in replacement: implement
/// <see cref="IBillingProvider"/> and select it in <c>DependencyInjection.AddBilling</c>
/// based on <c>BillingOptions.Provider</c>. Nothing else in the entitlement system needs to change.
/// </remarks>
internal sealed class NullBillingProvider : IBillingProvider
{
    private const string NotConfiguredReason =
        "No payment provider is configured yet. Contact us to upgrade.";

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, PlanTier targetTier, CancellationToken cancellationToken = default) =>
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
