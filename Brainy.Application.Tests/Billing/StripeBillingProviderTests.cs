using Brainy.Application.Billing;
using Brainy.Application.Options;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using Xunit;
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Application.Tests.Billing;

public sealed class StripeBillingProviderTests
{
    private const string WebhookSecret = "whsec_test_secret_only_used_locally";
    private const string UserId = "stripe-user-1";
    private const string CustomerId = "cus_test_123";
    private const string SubscriptionId = "sub_test_123";
    private const string MonthlyPriceId = "price_monthly_test";
    private const string YearlyPriceId = "price_yearly_test";

    private static readonly string ApiVersion = (string)typeof(StripeConfiguration).Assembly
        .GetType("Stripe.ApiVersion")!
        .GetField(
            "Current",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(null)!;

    private static BillingOptions CreateOptions() => new()
    {
        Provider = BillingProviderType.Stripe,
        ApiKey = "sk_test_fake_key_never_sent_anywhere",
        WebhookSigningSecret = WebhookSecret,
        ProMonthlyPriceId = MonthlyPriceId,
        ProYearlyPriceId = YearlyPriceId,
        CheckoutSuccessUrl = "https://app.example.test/Account/Manage?checkout=success",
        CheckoutCancelUrl = "https://app.example.test/Account/Manage?checkout=cancelled",
        PortalReturnUrl = "https://app.example.test/Account/Manage",
    };

    private static BrainyDbContext CreateDb(string name)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(name));
        return services.BuildServiceProvider().GetRequiredService<BrainyDbContext>();
    }

    private static StripeBillingProvider CreateProvider(
        BrainyDbContext db,
        TimeProvider? timeProvider = null,
        IStripeClient? stripeClient = null,
        BillingOptions? options = null) =>
        new(
            Microsoft.Extensions.Options.Options.Create(options ?? CreateOptions()),
            db,
            timeProvider ?? TimeProvider.System,
            NullLogger<StripeBillingProvider>.Instance,
            stripeClient);

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithValidSignature_IsValid()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithValidSignature_IsValid));
        var provider = CreateProvider(db);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(payload, EventUtility.GenerateSignatureHeader(payload, WebhookSecret));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithSignatureForADifferentPayload_IsRejected()
    {
        using var db = CreateDb(nameof(VerifyWebhookSignatureAsync_WithSignatureForADifferentPayload_IsRejected));
        var provider = CreateProvider(db);
        var signedPayload = MinimalEventJson("evt_1", "checkout.session.completed");
        var signature = EventUtility.GenerateSignatureHeader(signedPayload, WebhookSecret);

        var result = await provider.VerifyWebhookSignatureAsync(MinimalEventJson("evt_1", "customer.subscription.deleted"), signature);

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
        var options = CreateOptions();
        options.WebhookSigningSecret = null;
        var provider = CreateProvider(db, options: options);
        var payload = MinimalEventJson("evt_1", "checkout.session.completed");

        var result = await provider.VerifyWebhookSignatureAsync(payload, EventUtility.GenerateSignatureHeader(payload, WebhookSecret));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompleted_LinksAccountWithoutGrantingATier()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_CheckoutSessionCompleted_LinksAccountWithoutGrantingATier));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(CheckoutCompletedPayload("evt_checkout_1", UserId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().BeNull();
        parsed.BillingProviderCustomerId.Should().Be(CustomerId);
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutSessionCompletedInPaymentMode_IsIgnored()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_CheckoutSessionCompletedInPaymentMode_IsIgnored));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(CheckoutCompletedPayload("evt_checkout_2", UserId, "payment"));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedActive_MapsToProWithPeriodEndAndClearsGrace()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionUpdatedActive_MapsToProWithPeriodEndAndClearsGrace));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_updated_1", "customer.subscription.updated", "active", YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        parsed.ClearsGracePeriod.Should().BeTrue();
        parsed.BillingProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionUpdatedPastDue_KeepsProWithoutClearingGrace()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionUpdatedPastDue_KeepsProWithoutClearingGrace));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_updated_2", "customer.subscription.updated", "past_due", MonthlyPriceId, 1751328000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        parsed.ClearsGracePeriod.Should().BeFalse();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_SubscriptionDeleted_MapsToStarterDowngrade()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_SubscriptionDeleted_MapsToStarterDowngrade));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_deleted_1", "customer.subscription.deleted", "canceled", YearlyPriceId, 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Starter);
        parsed.ClearsGracePeriod.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_UnconfiguredPrice_DoesNotGrantPro()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_UnconfiguredPrice_DoesNotGrantPro));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_unknown_price", "customer.subscription.updated", "active", "price_not_configured", 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Starter);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_WithoutMetadata_ResolvesUserFromStoredCustomerId()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_WithoutMetadata_ResolvesUserFromStoredCustomerId));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_no_metadata", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, metadataUserId: null));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_WithUnknownCustomerAndNoMetadata_IsIgnored()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_WithUnknownCustomerAndNoMetadata_IsIgnored));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_orphan", "customer.subscription.updated", "active", YearlyPriceId, 1748736000, metadataUserId: null));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoicePaymentFailed_ResolvesUserByCustomerIdAndSetsGracePeriod()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoicePaymentFailed_ResolvesUserByCustomerIdAndSetsGracePeriod));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload("evt_invoice_failed_1", "invoice.payment_failed", nextPaymentAttempt: 1751328000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().BeNull();
        parsed.GracePeriodEndsAtUtc.Should().Be(new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc));
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

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload("evt_invoice_paid_1", "invoice.paid", periodEnd: 1748736000));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Pro);
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

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForStarter_IsUnsupportedAndNeverCallsStripe()
    {
        using var db = CreateDb(nameof(CreateCheckoutSessionAsync_ForStarter_IsUnsupportedAndNeverCallsStripe));
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
        result.RedirectUrl.Should().BeNull();
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

    private static string CheckoutCompletedPayload(string eventId, string? clientReferenceId, string mode = "subscription") =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_test_1",
              "object": "checkout.session",
              "mode": "{{mode}}",
              "client_reference_id": {{(clientReferenceId is null ? "null" : $"\"{clientReferenceId}\"")}},
              "customer": "{{CustomerId}}",
              "subscription": "{{SubscriptionId}}"
            }
          }
        }
        """;

    private static string SubscriptionPayload(
        string eventId,
        string eventType,
        string status,
        string priceId,
        long currentPeriodEnd,
        string? metadataUserId = UserId) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{SubscriptionId}}",
              "object": "subscription",
              "customer": "{{CustomerId}}",
              "status": "{{status}}",
              "metadata": {{(metadataUserId is null ? "{}" : $$"""{"brainy_user_id": "{{metadataUserId}}"}""")}},
              "items": {
                "object": "list",
                "data": [
                  {
                    "id": "si_test_1",
                    "object": "subscription_item",
                    "current_period_end": {{currentPeriodEnd}},
                    "price": { "id": "{{priceId}}", "object": "price" }
                  }
                ]
              }
            }
          }
        }
        """;

    private static string InvoicePayload(string eventId, string eventType, long? nextPaymentAttempt = null, long? periodEnd = null) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "in_test_1",
              "object": "invoice",
              "customer": "{{CustomerId}}"{{(nextPaymentAttempt.HasValue ? $",\n              \"next_payment_attempt\": {nextPaymentAttempt.Value}" : string.Empty)}}{{(periodEnd.HasValue ? $",\n              \"period_end\": {periodEnd.Value}" : string.Empty)}}
            }
          }
        }
        """;
}
