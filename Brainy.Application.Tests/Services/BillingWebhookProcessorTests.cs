using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Webhook-idempotency tests for issue #296: processing the same provider event id twice
/// must apply the plan change exactly once.
/// </summary>
public sealed class BillingWebhookProcessorTests
{
    private const string UserId = "webhook-user";
    private const string ValidSignature = "valid-signature";

    private static (IBillingWebhookProcessor Processor, BrainyDbContext Db) BuildServices(
        string dbName, ParsedBillingWebhookEvent? eventToReturn, out FakeBillingProvider provider)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserId));

        var fakeProvider = new FakeBillingProvider(eventToReturn);
        services.AddSingleton<IBillingProvider>(fakeProvider);
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        provider = fakeProvider;
        return (sp.GetRequiredService<IBillingWebhookProcessor>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidSignature_IsRejectedAndAppliesNothing()
    {
        var parsedEvent = new ParsedBillingWebhookEvent("evt_1", "subscription.updated", UserId, PlanTier.Pro, null);
        var (processor, db) = BuildServices(nameof(ProcessAsync_WithInvalidSignature_IsRejectedAndAppliesNothing), parsedEvent, out _);

        var result = await processor.ProcessAsync("{}", "wrong-signature");

        result.Accepted.Should().BeFalse();
        (await db.ProcessedWebhookEvents.CountAsync()).Should().Be(0);
        (await db.UserPlans.AnyAsync(p => p.UserId == UserId)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_SameEventIdTwice_AppliesPlanChangeOnlyOnce()
    {
        var parsedEvent = new ParsedBillingWebhookEvent("evt_same_id", "subscription.updated", UserId, PlanTier.Pro, null);
        var (processor, db) = BuildServices(
            nameof(ProcessAsync_SameEventIdTwice_AppliesPlanChangeOnlyOnce), parsedEvent, out var provider);

        var first = await processor.ProcessAsync("{\"id\":\"evt_same_id\"}", ValidSignature);
        first.Accepted.Should().BeTrue();
        first.Reason.Should().Be("applied");

        var userPlan = await db.UserPlans.SingleAsync(p => p.UserId == UserId);
        userPlan.Tier.Should().Be(PlanTier.Pro);
        (await db.ProcessedWebhookEvents.CountAsync()).Should().Be(1);

        // Demote back to Starter directly, bypassing the processor, so a second "applied"
        // result (a bug) would be observable as the tier flipping back to Pro.
        userPlan.Tier = PlanTier.Starter;
        await db.SaveChangesAsync();

        var second = await processor.ProcessAsync("{\"id\":\"evt_same_id\"}", ValidSignature);

        second.Accepted.Should().BeTrue();
        second.Reason.Should().Be("already_processed");
        (await db.ProcessedWebhookEvents.CountAsync()).Should().Be(1);
        (await db.UserPlans.SingleAsync(p => p.UserId == UserId)).Tier.Should().Be(PlanTier.Starter);
    }

    [Fact]
    public async Task ProcessAsync_WithUnrecognizedEventType_IsAcceptedAsIgnoredAndAppliesNothing()
    {
        var (processor, db) = BuildServices(
            nameof(ProcessAsync_WithUnrecognizedEventType_IsAcceptedAsIgnoredAndAppliesNothing), null, out _);

        var result = await processor.ProcessAsync("{}", ValidSignature);

        result.Accepted.Should().BeTrue();
        result.Reason.Should().Be("ignored_event_type");
        (await db.ProcessedWebhookEvents.CountAsync()).Should().Be(0);
    }
}
