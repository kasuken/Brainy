using AwesomeAssertions;
using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Options;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Covers <see cref="IPushSubscriptionService"/>: per-device registration keyed by a hashed
/// endpoint, user-scoped removal (one-click unsubscribe), and test-notification pruning of
/// expired subscriptions (issue #315's guardrail).
/// </summary>
public class PushSubscriptionServiceTests
{
    private const string UserA = "push-sub-user-a";
    private const string UserB = "push-sub-user-b";

    private static (IPushSubscriptionService sut, BrainyDbContext db, FakePushNotificationSender sender) BuildService(
        string dbName,
        string userId = UserA,
        WebPushOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options ?? new WebPushOptions
        {
            VapidSubject = "mailto:test@example.com",
            VapidPublicKey = "public-key",
            VapidPrivateKey = "private-key",
        }));

        var sender = new FakePushNotificationSender();
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IPushSubscriptionService>(), sp.GetRequiredService<BrainyDbContext>(), sender);
    }

    [Fact]
    public void GetVapidPublicKey_WhenConfigured_ReturnsPublicKeyOnly()
    {
        var (sut, _, _) = BuildService(nameof(GetVapidPublicKey_WhenConfigured_ReturnsPublicKeyOnly));

        sut.GetVapidPublicKey().Should().Be("public-key");
    }

    [Fact]
    public void GetVapidPublicKey_WhenNotConfigured_ReturnsNull()
    {
        var (sut, _, _) = BuildService(
            nameof(GetVapidPublicKey_WhenNotConfigured_ReturnsNull),
            options: new WebPushOptions());

        sut.GetVapidPublicKey().Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_NewEndpoint_CreatesRowWithHashedEndpoint()
    {
        var built = BuildService(nameof(RegisterAsync_NewEndpoint_CreatesRowWithHashedEndpoint));

        var result = await built.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/endpoint-1", "p256dh-key", "auth-key", "Chrome on Windows"));

        var stored = await built.db.PushSubscriptions.AsNoTracking().SingleAsync();
        stored.UserId.Should().Be(UserA);
        stored.Endpoint.Should().Be("https://push.example.com/endpoint-1");
        stored.EndpointHash.Should().HaveLength(64);
        stored.DeviceLabel.Should().Be("Chrome on Windows");
        result.Id.Should().Be(stored.Id);
    }

    [Fact]
    public async Task RegisterAsync_SameEndpointTwice_UpdatesInPlace_DoesNotDuplicate()
    {
        var built = BuildService(nameof(RegisterAsync_SameEndpointTwice_UpdatesInPlace_DoesNotDuplicate));

        await built.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/endpoint-1", "old-p256dh", "old-auth", "Old label"));
        await built.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/endpoint-1", "new-p256dh", "new-auth", "New label"));

        var rows = await built.db.PushSubscriptions.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].P256dh.Should().Be("new-p256dh");
        rows[0].DeviceLabel.Should().Be("New label");
    }

    [Fact]
    public async Task UnregisterAsync_OnlyRemovesTheCallingUsersOwnSubscription()
    {
        const string dbName = nameof(UnregisterAsync_OnlyRemovesTheCallingUsersOwnSubscription);
        var builtA = BuildService(dbName, UserA);
        var registeredA = await builtA.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/a", "k", "a", null));

        var builtB = BuildService(dbName, UserB);
        var registeredB = await builtB.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/b", "k", "a", null));

        // User B must not be able to remove user A's subscription by guessing/reusing its id.
        await builtB.sut.UnregisterAsync(registeredA.Id);
        (await builtA.db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Id == registeredA.Id))
            .Should().BeTrue("unregister must be scoped to the caller's own subscriptions");

        await builtA.sut.UnregisterAsync(registeredA.Id);
        (await builtA.db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Id == registeredA.Id))
            .Should().BeFalse();
        (await builtB.db.PushSubscriptions.AsNoTracking().AnyAsync(s => s.Id == registeredB.Id))
            .Should().BeTrue("removing one user's device must not affect another user's");
    }

    [Fact]
    public async Task SendTestNotificationAsync_NeverIncludesNoteOrTaskContent()
    {
        var built = BuildService(nameof(SendTestNotificationAsync_NeverIncludesNoteOrTaskContent));
        await built.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/x", "k", "a", null));

        var sent = await built.sut.SendTestNotificationAsync();

        sent.Should().BeTrue();
        built.sender.Sent.Should().ContainSingle();
        var payload = built.sender.Sent[0].Payload;
        payload.Category.Should().BeNull("a test send belongs to none of the three scheduled categories");
        payload.Heading.Should().NotBeNullOrWhiteSpace();
        payload.Body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendTestNotificationAsync_WhenPushServiceReportsExpired_PrunesTheSubscription()
    {
        var built = BuildService(nameof(SendTestNotificationAsync_WhenPushServiceReportsExpired_PrunesTheSubscription));
        built.sender.OutcomeSelector = (_, _) => PushSendResult.Expired(410);
        await built.sut.RegisterAsync(new RegisterPushSubscriptionDto(
            "https://push.example.com/gone", "k", "a", null));

        var sent = await built.sut.SendTestNotificationAsync();

        sent.Should().BeFalse();
        (await built.db.PushSubscriptions.AsNoTracking().AnyAsync())
            .Should().BeFalse("a 404/410 from the push service must prune the subscription, not retry it");
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WithNoSubscriptions_ReturnsEmpty()
    {
        var built = BuildService(nameof(GetSubscriptionsAsync_WithNoSubscriptions_ReturnsEmpty));

        (await built.sut.GetSubscriptionsAsync()).Should().BeEmpty();
    }
}
