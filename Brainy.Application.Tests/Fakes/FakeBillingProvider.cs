using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Domain.Enums;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IBillingProvider"/> that always verifies successfully and
/// returns a fixed, caller-supplied parsed event, so webhook-processing tests can exercise
/// <c>IBillingWebhookProcessor</c> idempotency without a real payment provider.
/// </summary>
internal sealed class FakeBillingProvider(ParsedBillingWebhookEvent? eventToReturn) : IBillingProvider
{
    public int ParseCallCount { get; private set; }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, PlanTier targetTier, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckoutSessionResult(false, null, "not implemented in test"));

    public Task<PortalSessionResult> CreatePortalSessionAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PortalSessionResult(false, null, "not implemented in test"));

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload, string signatureHeader, CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookVerificationResult(signatureHeader == "valid-signature", "invalid test signature"));

    public Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(string payload, CancellationToken cancellationToken = default)
    {
        ParseCallCount++;
        return Task.FromResult(eventToReturn);
    }
}
