using Brainy.Application.Analytics;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IAnalyticsService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class AnalyticsServiceTests
{
    private const string DefaultUserId = "analytics-user-1";
    private const string OtherUserId = "analytics-user-2";
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static (IAnalyticsService Sut, BrainyDbContext Db, FixedTimeProvider Clock) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        var clock = new FixedTimeProvider(FixedNow);
        services.AddSingleton<TimeProvider>(clock);

        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IAnalyticsService>(), provider.GetRequiredService<BrainyDbContext>(), clock);
    }

    [Fact]
    public async Task TrackAsync_WithUnknownEventName_Throws()
    {
        var (sut, _, _) = BuildService(nameof(TrackAsync_WithUnknownEventName_Throws));

        var act = () => sut.TrackAsync(DefaultUserId, "not.a.real.event");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TrackAsync_WithConsentDefaulted_WritesEvent()
    {
        var (sut, db, _) = BuildService(nameof(TrackAsync_WithConsentDefaulted_WritesEvent));

        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.CaptureCreated);

        (await db.ProductEvents.CountAsync()).Should().Be(1);
        var stored = await db.ProductEvents.SingleAsync();
        stored.UserId.Should().Be(DefaultUserId);
        stored.EventName.Should().Be(AnalyticsEvents.CaptureCreated);
    }

    [Fact]
    public async Task TrackAsync_WhenUserOptedOut_DoesNotWriteEvent()
    {
        var (sut, db, _) = BuildService(nameof(TrackAsync_WhenUserOptedOut_DoesNotWriteEvent));

        await sut.SetCurrentUserOptedInAsync(false);
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.CaptureCreated);

        (await db.ProductEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetCurrentUserOptedInAsync_ThenIsCurrentUserOptedInAsync_RoundTrips()
    {
        var (sut, _, _) = BuildService(nameof(SetCurrentUserOptedInAsync_ThenIsCurrentUserOptedInAsync_RoundTrips));

        (await sut.IsCurrentUserOptedInAsync()).Should().BeTrue("analytics defaults to opted in");

        await sut.SetCurrentUserOptedInAsync(false);
        (await sut.IsCurrentUserOptedInAsync()).Should().BeFalse();

        await sut.SetCurrentUserOptedInAsync(true);
        (await sut.IsCurrentUserOptedInAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task TrackOnceAsync_FiresOnlyOncePerUser()
    {
        var (sut, db, _) = BuildService(nameof(TrackOnceAsync_FiresOnlyOncePerUser));

        await sut.TrackOnceAsync(DefaultUserId, AnalyticsEvents.FirstCaptureCreated);
        await sut.TrackOnceAsync(DefaultUserId, AnalyticsEvents.FirstCaptureCreated);
        await sut.TrackOnceAsync(DefaultUserId, AnalyticsEvents.FirstCaptureCreated);

        (await db.ProductEvents.CountAsync(e => e.EventName == AnalyticsEvents.FirstCaptureCreated))
            .Should().Be(1);
    }

    [Fact]
    public async Task HasEventOccurredAsync_ReflectsWrites()
    {
        var (sut, _, _) = BuildService(nameof(HasEventOccurredAsync_ReflectsWrites));

        (await sut.HasEventOccurredAsync(DefaultUserId, AnalyticsEvents.TaskCreated)).Should().BeFalse();

        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.TaskCreated);

        (await sut.HasEventOccurredAsync(DefaultUserId, AnalyticsEvents.TaskCreated)).Should().BeTrue();
    }

    [Fact]
    public async Task TrackAsync_WithOversizedProperties_Throws()
    {
        var (sut, _, _) = BuildService(nameof(TrackAsync_WithOversizedProperties_Throws));
        var properties = new Dictionary<string, object?> { ["huge"] = new string('x', 3000) };

        var act = () => sut.TrackAsync(DefaultUserId, AnalyticsEvents.CaptureCreated, properties);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetActivationFunnelAsync_ComputesCountsAcrossUsers()
    {
        var (sut, _, _) = BuildService(nameof(GetActivationFunnelAsync_ComputesCountsAcrossUsers));

        await sut.TrackOnceAsync(DefaultUserId, AnalyticsEvents.FirstCaptureCreated);
        await sut.TrackOnceAsync(DefaultUserId, AnalyticsEvents.FirstTaskCreated);
        await sut.TrackOnceAsync(OtherUserId, AnalyticsEvents.FirstCaptureCreated);

        var funnel = await sut.GetActivationFunnelAsync();

        funnel.UsersWithAnyEvent.Should().Be(2);
        funnel.UsersWithFirstCapture.Should().Be(2);
        funnel.UsersWithFirstTask.Should().Be(1);
        funnel.UsersWithFirstProcessedItem.Should().Be(0);
        funnel.FirstTaskRate.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public async Task GetAiUsageSummaryAsync_ComputesFailureAndEditRates()
    {
        var (sut, _, _) = BuildService(nameof(GetAiUsageSummaryAsync_ComputesFailureAndEditRates));

        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.AiRequestSubmitted);
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.AiRequestSubmitted);
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.AiRequestFailed);
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.AiSuggestionReviewed,
            new Dictionary<string, object?> { ["edited"] = true });
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.AiSuggestionReviewed,
            new Dictionary<string, object?> { ["edited"] = false });

        var usage = await sut.GetAiUsageSummaryAsync();

        usage.RequestsSubmitted.Should().Be(2);
        usage.RequestsFailed.Should().Be(1);
        usage.FailureRate.Should().BeApproximately(0.5, 0.0001);
        usage.SuggestionsReviewed.Should().Be(2);
        usage.SuggestionsEdited.Should().Be(1);
        usage.EditRate.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public async Task GetRetentionSummaryAsync_CountsUsersWithLaterActivityAsRetained()
    {
        var (sut, _, clock) = BuildService(nameof(GetRetentionSummaryAsync_CountsUsersWithLaterActivityAsRetained));

        // Both users' first event happens at t=0, so both are eligible for the D7 cohort
        // once "now" passes t=7d. Only DefaultUserId returns after the D7 boundary.
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.CaptureCreated);
        await sut.TrackAsync(OtherUserId, AnalyticsEvents.CaptureCreated);

        clock.Advance(TimeSpan.FromDays(10));
        await sut.TrackAsync(DefaultUserId, AnalyticsEvents.CaptureCreated);

        clock.Advance(TimeSpan.FromDays(1));

        var retention = await sut.GetRetentionSummaryAsync();

        retention.Day7CohortSize.Should().Be(2);
        retention.Day7Retained.Should().Be(1);
        retention.Day30CohortSize.Should().Be(0);
    }
}
