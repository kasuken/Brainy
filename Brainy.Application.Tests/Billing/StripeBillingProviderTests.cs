using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Options;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Billing;

/// <summary>
/// Tests for the live Stripe provider, exercised through <c>AddBilling</c> exactly as the Web
/// host resolves it. Every case here is offline: signatures are generated with Stripe's own
/// <c>EventUtility</c> and payloads are parsed locally, so nothing contacts the Stripe API.
/// </summary>
public sealed class StripeBillingProviderTests
{
    private const string UserId = "stripe-user";
    private const string CustomerId = "cus_TEST123";
    private const string SubscriptionId = "sub_TEST123";
    private const string YearlyPriceId = "price_1UFw4cBHFTxI6VL5PBM7hd7e";
    private const string MonthlyPriceId = "price_1UFw4cBHFTxI6VL5iDA7yaI1";
    private const string WebhookSecret = "whsec_testsecretvaluethatislongenough";

    private static (IBillingProvider Provider, BrainyDbContext Db) BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddBilling(BuildStripeConfiguration());

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IBillingProvider>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static IConfiguration BuildStripeConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Billing:Provider"] = "Stripe",
            ["Billing:ApiKey"] = "sk_test_key",
            ["Billing:WebhookSigningSecret"] = WebhookSecret,
            ["Billing:ProMonthlyPriceId"] = MonthlyPriceId,
            ["Billing:ProYearlyPriceId"] = YearlyPriceId,
            ["Billing:CheckoutSuccessUrl"] = "https://brainy.test/Account/Manage?checkout=success",
            ["Billing:CheckoutCancelUrl"] = "https://brainy.test/Account/Manage?checkout=cancelled",
            ["Billing:PortalReturnUrl"] = "https://brainy.test/Account/Manage",
        };

        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

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
                    "id": "si_TEST123",
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

    private static string CheckoutCompletedPayload(string eventId, string? clientReferenceId) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_TEST123",
              "object": "checkout.session",
              "client_reference_id": {{(clientReferenceId is null ? "null" : $"\"{clientReferenceId}\"")}},
              "customer": "{{CustomerId}}",
              "subscription": "{{SubscriptionId}}",
              "metadata": {}
            }
          }
        }
        """;

    private static string SignatureFor(string payload) =>
        Stripe.EventUtility.GenerateSignatureHeader(payload, WebhookSecret);

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithStripeSignedHeader_Succeeds()
    {
        var (provider, _) = BuildProvider(nameof(VerifyWebhookSignatureAsync_WithStripeSignedHeader_Succeeds));
        var payload = CheckoutCompletedPayload("evt_sig_ok", UserId);

        var result = await provider.VerifyWebhookSignatureAsync(payload, SignatureFor(payload));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_WithTamperedPayload_Fails()
    {
        var (provider, _) = BuildProvider(nameof(VerifyWebhookSignatureAsync_WithTamperedPayload_Fails));
        var payload = CheckoutCompletedPayload("evt_sig_tampered", UserId);
        var signature = SignatureFor(payload);

        // The signature is valid, but for a different body: an attacker replaying a captured
        // header against edited content must not be able to change anyone's plan.
        var result = await provider.VerifyWebhookSignatureAsync(
            CheckoutCompletedPayload("evt_sig_tampered", "someone-else"), signature);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForActiveYearlySubscription_GrantsProWithPeriodEnd()
    {
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForActiveYearlySubscription_GrantsProWithPeriodEnd));
        const long periodEnd = 1790000000;

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_active", "customer.subscription.updated", "active", YearlyPriceId, periodEnd));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().Be(PlanTier.Pro);
        parsed.PeriodEndsAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(periodEnd).UtcDateTime);
        parsed.ProviderCustomerId.Should().Be(CustomerId);
        parsed.ProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForPastDueSubscription_KeepsPro()
    {
        // Stripe retries a failed charge for days before cancelling; cutting access at the
        // first failure would lock out a user whose card merely expired.
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForPastDueSubscription_KeepsPro));

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_pastdue", "customer.subscription.updated", "past_due", MonthlyPriceId, 1790000000));

        parsed!.NewTier.Should().Be(PlanTier.Pro);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForDeletedSubscription_DropsToStarter()
    {
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForDeletedSubscription_DropsToStarter));

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_deleted", "customer.subscription.deleted", "canceled", YearlyPriceId, 1790000000));

        parsed!.NewTier.Should().Be(PlanTier.Starter);
        parsed.PeriodEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForUnconfiguredPrice_DoesNotGrantPro()
    {
        // A price created in the Stripe Dashboard but never configured here must not be able
        // to hand out a paid tier Brainy has no entitlement row for.
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForUnconfiguredPrice_DoesNotGrantPro));

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_unknown_price", "customer.subscription.updated", "active", "price_NOT_CONFIGURED", 1790000000));

        parsed!.NewTier.Should().Be(PlanTier.Starter);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_WithoutMetadata_ResolvesUserFromStoredCustomerId()
    {
        var (provider, db) = BuildProvider(nameof(ParseWebhookEventAsync_WithoutMetadata_ResolvesUserFromStoredCustomerId));
        db.UserPlans.Add(new UserPlan { UserId = UserId, Tier = PlanTier.Pro, BillingProviderCustomerId = CustomerId });
        await db.SaveChangesAsync();

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_no_metadata", "customer.subscription.updated", "active", YearlyPriceId, 1790000000, metadataUserId: null));

        parsed!.TargetUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_WithUnknownCustomerAndNoMetadata_IsIgnored()
    {
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_WithUnknownCustomerAndNoMetadata_IsIgnored));

        var parsed = await provider.ParseWebhookEventAsync(
            SubscriptionPayload("evt_sub_orphan", "customer.subscription.updated", "active", YearlyPriceId, 1790000000, metadataUserId: null));

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForCompletedCheckout_LinksAccountWithoutGrantingATier()
    {
        // The tier comes from the subscription event, which carries the authoritative price;
        // a completed checkout only tells us which Stripe customer this user is.
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForCompletedCheckout_LinksAccountWithoutGrantingATier));

        var parsed = await provider.ParseWebhookEventAsync(CheckoutCompletedPayload("evt_checkout_done", UserId));

        parsed.Should().NotBeNull();
        parsed!.TargetUserId.Should().Be(UserId);
        parsed.NewTier.Should().BeNull();
        parsed.ProviderCustomerId.Should().Be(CustomerId);
        parsed.ProviderSubscriptionId.Should().Be(SubscriptionId);
    }

    [Fact]
    public async Task ParseWebhookEventAsync_ForUnrelatedEventType_IsIgnored()
    {
        var (provider, _) = BuildProvider(nameof(ParseWebhookEventAsync_ForUnrelatedEventType_IsIgnored));

        var parsed = await provider.ParseWebhookEventAsync(
            """{"id":"evt_ping","object":"event","type":"payment_intent.succeeded","data":{"object":{"id":"pi_1","object":"payment_intent"}}}""");

        parsed.Should().BeNull();
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_ForStarter_IsUnsupported()
    {
        var (provider, _) = BuildProvider(nameof(CreateCheckoutSessionAsync_ForStarter_IsUnsupported));

        var result = await provider.CreateCheckoutSessionAsync(UserId, PlanTier.Starter);

        result.Supported.Should().BeFalse();
        result.UnsupportedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreatePortalSessionAsync_WithoutAStripeCustomer_IsUnsupported()
    {
        var (provider, _) = BuildProvider(nameof(CreatePortalSessionAsync_WithoutAStripeCustomer_IsUnsupported));

        var result = await provider.CreatePortalSessionAsync(UserId);

        result.Supported.Should().BeFalse();
        result.RedirectUrl.Should().BeNull();
    }

    [Fact]
    public void AddBilling_WithStripeSelectedButSettingsMissing_FailsAtStartup()
    {
        // A half-configured payment provider must not start: the alternative is a real user
        // discovering it mid-purchase.
        var configuration = BuildStripeConfiguration(new Dictionary<string, string?>
        {
            ["Billing:ProYearlyPriceId"] = null,
            ["Billing:WebhookSigningSecret"] = null,
        });

        var act = () => new ServiceCollection().AddBilling(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProYearlyPriceId*")
            .WithMessage("*WebhookSigningSecret*");
    }
}
