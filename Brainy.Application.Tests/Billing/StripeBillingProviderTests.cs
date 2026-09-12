using Brainy.Application.Billing;
using Brainy.Application.Options;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stripe;
using Xunit;
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Application.Tests.Billing;

/// <summary>
/// Covers <see cref="StripeBillingProvider"/>: webhook signature verification (issue #308
/// acceptance criteria "a forged webhook payload is rejected" and "replayed events remain
/// idempotent" — idempotency itself is <see cref="Services.BillingWebhookProcessorTests"/>'s
/// job, this covers the signature check it depends on), event-to-<see cref="Domain.Enums.PlanTier"/>
/// mapping, and the checkout/portal session seams. Every Stripe call here goes through
/// <see cref="FakeStripeClient"/> or pure local signature math (<see cref="EventUtility"/>) —
/// nothing in this file ever calls the real Stripe API.
/// </summary>
public sealed class StripeBillingProviderTests
{
    private const string WebhookSecret = "whsec_test_secret_only_used_locally";
    private const string UserId = "stripe-user-1";
    private const string CustomerId = "cus_test_123";
    private const string SubscriptionId = "sub_test_123";

    /// <summary>
    /// Stripe.net's own current API version, read via reflection so test event payloads carry
    /// one it recognizes as a match — <c>EventUtility.ParseEvent</c> otherwise throws trying to
    /// compare against a missing <c>api_version</c>. A real webhook delivery always carries the
    /// Stripe account's api_version, so this is purely a test-fixture concern.
    /// </summary>
    private static readonly string ApiVersion = (string)typeof(StripeConfiguration).Assembly
        .GetType("Stripe.ApiVersion")!
        .GetField(
            "Current",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(null)!;

    private static BillingOptions Options() => new()
    {
        Provider = BillingProviderType.Stripe,
        ApiKey = "sk_test_fake_key_never_sent_anywhere",
        WebhookSigningSecret = WebhookSecret,
        ProPriceId = "price_pro_test",
        AppBaseUrl = "https://app.example.test",
    };

    private static BrainyDbContext CreateDb(string name)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(name));
        return services.BuildServiceProvider().GetRequiredService<BrainyDbContext>();
    }

    private static StripeBillingProvider CreateProvider(
        BrainyDbContext db, TimeProvider? timeProvider = null, IStripeClient? stripeClient = null, BillingOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options()), db, timeProvider ?? TimeProvider.System, stripeClient);

    // ---- Webhook signature verification --------------------------------------------------

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithValidSignature_IsValid()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithValidSignature_IsValid));
        var provider = CreateProvider(db);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");
        var signature = EventUtility.GenerateSignatureHeader(payload, WebhookSecret);

        var result = await provider.VerifyWebhookSignatureAsync(payload, signature);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithSignatureForADifferentPayload_IsRejected()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithSignatureForADifferentPayload_IsRejected));
        var provider = CreateProvider(db);
        var signedPayload = MinimalEventJson("evt_1", "checkout.session.completed");
        var signature = EventUtility.GenerateSignatureHeader(signedPayload, WebhookSecret);

        // A forged payload: same signature header, but the body it was computed over changed —
        // e.g. an attacker replaying a captured signature against a tampered plan-change event.
        var forgedPayload = MinimalEventJson("evt_1", "customer.subscription.deleted");

        var result = await provider.VerifyWebhookSignatureAsync(forgedPayload, signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithWrongSigningSecret_IsRejected()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithWrongSigningSecret_IsRejected));
        var provider = CreateProvider(db);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");
        var signature = EventUtility.GenerateSignatureHeader(payload, "whsec_a_completely_different_secret");

        var result = await provider.VerifyWebhookSignatureAsync(payload, signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithMissingSignatureHeader_IsRejected()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithMissingSignatureHeader_IsRejected));
        var provider = CreateProvider(db);

        var result = await provider.VerifyWebhookSignatureAsync(MinimalEventJson("evt_1", "checkout.session.completed"), "");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithNoSigningSecretConfigured_IsRejected()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithNoSigningSecretConfigured_IsRejected));
        var options = Options();
        options.WebhookSigningSecret = null;
        var provider = CreateProvider(db, options: options);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(payload, EventUtility.GenerateSignatureHeader(payload, WebhookSecret));

        result.IsValid.Should().BeFalse();
    }

    // ---- Event parsing / PlanTier mapping -------------------------------------------------

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompleted_MapsToProUpgrade()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_CheckoutSessionCompleted_MapsToProUpgrade));
        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_checkout_1",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "checkout.session.completed",
              "data": {
                "object": {
                  "id": "cs_test_1",
                  "object": "checkout.session",
                  "mode": "subscription",
                  "client_reference_id": "{{UserId}}",
                  "customer": "{{CustomerId}}",
                  "subscription": "{{SubscriptionId}}"
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().NotBeNull();
        parsed!.ProviderEventId.Should().Be("evt_checkout_1");
        parsed.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompletedInPaymentMode_IsIgnored()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_CheckoutSessionCompletedInPaymentMode_IsIgnored));
        var provider = CreateProvider(db);

        // Brainy only ever starts subscription-mode checkouts; a one-off "payment" mode session
        // (not something Brainy's own checkout creates) must not be interpreted as a plan change.
        var payload =
            $$"""
            {
              "id": "evt_checkout_2",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "checkout.session.completed",
              "data": {
                "object": {
                  "id": "cs_test_2",
                  "object": "checkout.session",
                  "mode": "payment",
                  "client_reference_id": "{{UserId}}",
                  "customer": "{{CustomerId}}"
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedActive_MapsToProWithPeriodEndAndClearsGrace()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionUpdatedActive_MapsToProWithPeriodEndAndClearsGrace));
        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_sub_updated_1",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "id": "{{SubscriptionId}}",
                  "object": "subscription",
                  "status": "active",
                  "customer": "{{CustomerId}}",
                  "metadata": { "brainy_user_id": "{{UserId}}" },
                  "items": {
                    "object": "list",
                    "data": [
                      { "id": "si_1", "object": "subscription_item", "current_period_end": 1748736000 }
                    ]
                  }
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedPastDue_DoesNotChangeTierYet()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionUpdatedPastDue_DoesNotChangeTierYet));
        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_sub_updated_2",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "id": "{{SubscriptionId}}",
                  "object": "subscription",
                  "status": "past_due",
                  "customer": "{{CustomerId}}",
                  "metadata": { "brainy_user_id": "{{UserId}}" }
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        // Still linked/recorded (for idempotency and housekeeping) but no tier change: the
        // grace period is what invoice.payment_failed records, and the eventual downgrade
        // comes from customer.subscription.deleted once Stripe gives up retrying.
        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().BeNull();
        parsed.ClearsGracePeriod.Should().BeFalse();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionDeleted_MapsToStarterDowngrade()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionDeleted_MapsToStarterDowngrade));
        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_sub_deleted_1",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "customer.subscription.deleted",
              "data": {
                "object": {
                  "id": "{{SubscriptionId}}",
                  "object": "subscription",
                  "status": "canceled",
                  "customer": "{{CustomerId}}",
                  "metadata": { "brainy_user_id": "{{UserId}}" }
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Starter);
        parsed.ClearsGracePeriod.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaymentFailed_ResolvesUserByCustomerIdAndSetsGracePeriod()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoicePaymentFailed_ResolvesUserByCustomerIdAndSetsGracePeriod));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();

        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_invoice_failed_1",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "invoice.payment_failed",
              "data": {
                "object": {
                  "id": "in_1",
                  "object": "invoice",
                  "customer": "{{CustomerId}}",
                  "next_payment_attempt": 1751328000
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().BeNull("a failed charge alone must not downgrade Pro access immediately");
        parsed.GracePeriodEndsAtUtc.Should().Be(new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaymentFailedForUnknownCustomer_IsIgnored()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoicePaymentFailedForUnknownCustomer_IsIgnored));
        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_invoice_failed_2",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "invoice.payment_failed",
              "data": {
                "object": { "id": "in_2", "object": "invoice", "customer": "cus_unknown" }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaid_ResolvesUserAndClearsGracePeriod()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoicePaid_ResolvesUserAndClearsGracePeriod));
        db.UserPlans.Add(new UserPlan
        {
            UserId = UserId,
            Tier = PlanTier.Pro,
            BillingProviderCustomerId = CustomerId,
            GracePeriodEndsAtUtc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var provider = CreateProvider(db);

        var payload =
            $$"""
            {
              "id": "evt_invoice_paid_1",
              "object": "event",
              "api_version": "{{ApiVersion}}",
              "type": "invoice.paid",
              "data": {
                "object": {
                  "id": "in_3",
                  "object": "invoice",
                  "customer": "{{CustomerId}}",
                  "period_end": 1748736000
                }
              }
            }
            """;

        var parsed = await provider.ParseWebhookEventAsync(payload);

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        parsed.ClearsGracePeriod.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_UnrecognizedEventType_ReturnsNull()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_UnrecognizedEventType_ReturnsNull));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(MinimalEventJson("evt_x", "customer.created"));

        parsed.Should().BeNull();
    }

    // ---- Checkout / portal sessions -------------------------------------------------------

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForNonProTier_IsUnsupportedAndNeverCallsStripe()
    {
        using var db = CreateDb(nameof(CreateCheckoutSessionAsync_ForNonProTier_IsUnsupportedAndNeverCallsStripe));
        var fakeClient = new FakeStripeClient(_ => throw new InvalidOperationException("Should not call Stripe."));
        var provider = CreateProvider(db, stripeClient: fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(UserId, PlanTier.Starter);

        result.Supported.Should().BeFalse();
        result.RedirectUrl.Should().BeNull();
        fakeClient.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForPro_ReturnsStripesHostedCheckoutUrl()
    {
        using var db = CreateDb(nameof(CreateCheckoutSessionAsync_ForPro_ReturnsStripesHostedCheckoutUrl));
        const string checkoutUrl = "https://checkout.stripe.com/c/pay/cs_test_1";
        var fakeClient = new FakeStripeClient(_ => new Stripe.Checkout.Session { Id = "cs_test_1", Url = checkoutUrl });
        var provider = CreateProvider(db, stripeClient: fakeClient);

        var result = await provider.CreateCheckoutSessionAsync(UserId, PlanTier.Pro);

        result.Supported.Should().BeTrue();
        result.RedirectUrl.Should().Be(checkoutUrl);
        fakeClient.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Path == "/v1/checkout/sessions");
    }

    [Fact]
    public async Task CreatePortalSessionAsync_WithNoStoredCustomerId_IsUnsupportedAndNeverCallsStripe()
    {
        using var db = CreateDb(nameof(CreatePortalSessionAsync_WithNoStoredCustomerId_IsUnsupportedAndNeverCallsStripe));
        var fakeClient = new FakeStripeClient(_ => throw new InvalidOperationException("Should not call Stripe."));
        var provider = CreateProvider(db, stripeClient: fakeClient);

        var result = await provider.CreatePortalSessionAsync(UserId);

        result.Supported.Should().BeFalse();
        fakeClient.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePortalSessionAsync_WithStoredCustomerId_ReturnsStripesHostedPortalUrl()
    {
        using var db = CreateDb(nameof(CreatePortalSessionAsync_WithStoredCustomerId_ReturnsStripesHostedPortalUrl));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();

        const string portalUrl = "https://billing.stripe.com/p/session/test_1";
        var fakeClient = new FakeStripeClient(_ => new Stripe.BillingPortal.Session { Id = "bps_1", Url = portalUrl });
        var provider = CreateProvider(db, stripeClient: fakeClient);

        var result = await provider.CreatePortalSessionAsync(UserId);

        result.Supported.Should().BeTrue();
        result.RedirectUrl.Should().Be(portalUrl);
        fakeClient.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.Path == "/v1/billing_portal/sessions");
    }

    private static string MinimalEventJson(string id, string type) =>
        $$"""
        {
          "id": "{{id}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{type}}",
          "data": { "object": { "id": "obj_1", "object": "customer" } }
        }
        """;
}
