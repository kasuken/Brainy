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
    private const string BrainyMetadataKey = "brainy_user_id";

    // Another product (LearnStack) billed through the same Stripe account: its events reach
    // Brainy's endpoint too, validly signed with Brainy's own endpoint secret.
    private const string OtherProductMetadataKey = "learnstack_user_id";
    private const string OtherProductUserId = "learnstack-user-1";
    private const string OtherProductPriceId = "price_learnstack_pro";
    private const string OtherProductSubscriptionId = "sub_learnstack_1";

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

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_failed_1", "invoice.payment_failed", nextPaymentAttempt: 1751328000, linePriceId: YearlyPriceId));

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

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_paid_1", "invoice.paid", periodEnd: 1748736000, linePriceId: YearlyPriceId));

        parsed.Should().NotBeNull();
        parsed!.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        parsed.ClearsGracePeriod.Should().BeTrue();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoiceWithBrainySubscriptionMetadata_ResolvesUserFromTheMetadata()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoiceWithBrainySubscriptionMetadata_ResolvesUserFromTheMetadata));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_metadata", "invoice.paid", periodEnd: 1748736000, subscriptionMetadataUserId: UserId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_InvoiceForTheStoredSubscription_ResolvesUserWithoutAConfiguredPrice()
    {
        using var db = CreateDb(nameof(ParseWebhookEventAsync_InvoiceForTheStoredSubscription_ResolvesUserWithoutAConfiguredPrice));
        db.UserPlans.Add(new UserPlan
        {
            UserId = UserId,
            Tier = PlanTier.Pro,
            BillingProviderCustomerId = CustomerId,
            BillingProviderSubscriptionId = SubscriptionId,
        });
        await db.SaveChangesAsync();
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            InvoicePayload("evt_invoice_stored_sub", "invoice.payment_failed", nextPaymentAttempt: 1751328000, subscriptionId: SubscriptionId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_CheckoutFromAnotherProduct_IsIgnored()
    {
        // client_reference_id is set by every product's checkout, so it alone must never be
        // read as a Brainy user id (on SQL Server it would also fail the UserPlan foreign key).
        using var db = CreateDb(nameof(ParseWebhookEventAsync_CheckoutFromAnotherProduct_IsIgnored));
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(
            CheckoutCompletedPayload("evt_other_checkout", OtherProductUserId, metadataKey: OtherProductMetadataKey));

        parsed.Should().BeNull();
    }

    [Theory]
    [InlineData("customer.subscription.created")]
    [InlineData("customer.subscription.updated")]
    [InlineData("customer.subscription.deleted")]
    public async Task ParseWebhookEventAsync_AnotherProductsSubscriptionOnABrainyCustomer_IsIgnored(string eventType)
    {
        // Same Stripe customer as a paying Brainy user. Before this was guarded, an unknown
        // price mapped to Starter and a deletion downgraded the Brainy user.
        using var db = CreateDb(nameof(ParseWebhookEventAsync_AnotherProductsSubscriptionOnABrainyCustomer_IsIgnored) + eventType);
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId, BillingProviderSubscriptionId = SubscriptionId });
        await db.SaveChangesAsync();
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(SubscriptionPayload(
            "evt_other_subscription",
            eventType,
            "active",
            OtherProductPriceId,
            1748736000,
            metadataUserId: OtherProductUserId,
            metadataKey: OtherProductMetadataKey,
            subscriptionId: OtherProductSubscriptionId));

        parsed.Should().BeNull();
    }

    [Theory]
    [InlineData("invoice.paid")]
    [InlineData("invoice.payment_failed")]
    public async Task ParseWebhookEventAsync_AnotherProductsInvoiceOnABrainyCustomer_IsIgnored(string eventType)
    {
        // Before this was guarded, a paid invoice for another product granted Brainy Pro and a
        // failed one put the Brainy user into a grace period.
        using var db = CreateDb(nameof(ParseWebhookEventAsync_AnotherProductsInvoiceOnABrainyCustomer_IsIgnored) + eventType);
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Starter, BillingProviderCustomerId = CustomerId, BillingProviderSubscriptionId = SubscriptionId });
        await db.SaveChangesAsync();
        var provider = CreateProvider(db);

        var parsed = await provider.ParseWebhookEventAsync(InvoicePayload(
            "evt_other_invoice",
            eventType,
            nextPaymentAttempt: 1751328000,
            periodEnd: 1748736000,
            subscriptionId: OtherProductSubscriptionId,
            subscriptionMetadataUserId: OtherProductUserId,
            metadataKey: OtherProductMetadataKey,
            linePriceId: OtherProductPriceId));

        parsed.Should().BeNull();
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
    public async Task CreateCheckoutSessionAsync_ForAFirstPurchase_SeedsTheCustomerWithTheAccountEmail()
    {
        using var db = CreateDb(nameof(CreateCheckoutSessionAsync_ForAFirstPurchase_SeedsTheCustomerWithTheAccountEmail));
        var fakeClient = new FakeStripeClient(_ => new Stripe.Checkout.Session { Id = "cs_test_1", Url = "https://checkout.stripe.com/c/pay/cs_test_1" });
        var provider = CreateProvider(db, stripeClient: fakeClient);

        await provider.CreateCheckoutSessionAsync(UserId, PlanTier.Pro, Brainy.Domain.Enums.BillingInterval.Yearly, "account@example.test");

        var sent = fakeClient.SentOptions.Should().ContainSingle().Which.Should().BeOfType<Stripe.Checkout.SessionCreateOptions>().Subject;
        sent.CustomerEmail.Should().Be("account@example.test");
        sent.Customer.Should().BeNull();
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForAnExistingCustomer_SendsNoEmailAlongsideTheCustomerId()
    {
        // Stripe rejects customer and customer_email together, so an account email must be
        // dropped once the user has a customer record rather than sent with it.
        using var db = CreateDb(nameof(CreateCheckoutSessionAsync_ForAnExistingCustomer_SendsNoEmailAlongsideTheCustomerId));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Starter, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();

        var fakeClient = new FakeStripeClient(_ => new Stripe.Checkout.Session { Id = "cs_test_1", Url = "https://checkout.stripe.com/c/pay/cs_test_1" });
        var provider = CreateProvider(db, stripeClient: fakeClient);

        await provider.CreateCheckoutSessionAsync(UserId, PlanTier.Pro, Brainy.Domain.Enums.BillingInterval.Yearly, "account@example.test");

        var sent = fakeClient.SentOptions.Should().ContainSingle().Which.Should().BeOfType<Stripe.Checkout.SessionCreateOptions>().Subject;
        sent.Customer.Should().Be(CustomerId);
        sent.CustomerEmail.Should().BeNull();
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

    private static string CheckoutCompletedPayload(
        string eventId,
        string? clientReferenceId,
        string mode = "subscription",
        string metadataKey = BrainyMetadataKey) =>
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
              "metadata": {{MetadataJson(metadataKey, clientReferenceId)}},
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
        string? metadataUserId = UserId,
        string metadataKey = BrainyMetadataKey,
        string subscriptionId = SubscriptionId) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{subscriptionId}}",
              "object": "subscription",
              "customer": "{{CustomerId}}",
              "status": "{{status}}",
              "metadata": {{MetadataJson(metadataKey, metadataUserId)}},
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

    /// <summary>
    /// An invoice event in the shape Stripe's current API sends: the billed subscription (and a
    /// snapshot of its metadata) under <c>parent.subscription_details</c>, and each line's
    /// price under <c>pricing.price_details</c>.
    /// </summary>
    private static string InvoicePayload(
        string eventId,
        string eventType,
        long? nextPaymentAttempt = null,
        long? periodEnd = null,
        string subscriptionId = "sub_unrecorded",
        string? subscriptionMetadataUserId = null,
        string metadataKey = BrainyMetadataKey,
        string linePriceId = "price_unconfigured") =>
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
              "customer": "{{CustomerId}}",
              "parent": {
                "type": "subscription_details",
                "subscription_details": {
                  "subscription": "{{subscriptionId}}",
                  "metadata": {{MetadataJson(metadataKey, subscriptionMetadataUserId)}}
                }
              },
              "lines": {
                "object": "list",
                "data": [
                  {
                    "id": "il_test_1",
                    "object": "line_item",
                    "pricing": {
                      "type": "price_details",
                      "price_details": { "price": "{{linePriceId}}", "product": "prod_test_1" }
                    }
                  }
                ]
              }{{(nextPaymentAttempt.HasValue ? $",\n              \"next_payment_attempt\": {nextPaymentAttempt.Value}" : string.Empty)}}{{(periodEnd.HasValue ? $",\n              \"period_end\": {periodEnd.Value}" : string.Empty)}}
            }
          }
        }
        """;

    private static string MetadataJson(string key, string? userId) =>
        userId is null ? "{}" : $$"""{"{{key}}": "{{userId}}"}""";
}
