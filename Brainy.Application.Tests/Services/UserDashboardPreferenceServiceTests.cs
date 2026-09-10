using Brainy.Application.DTOs.Dashboard;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IUserDashboardPreferenceService"/> resolved via the real DI
/// container with an EF Core InMemory database. Each test uses a unique database name.
/// </summary>
public class UserDashboardPreferenceServiceTests
{
    private const string DefaultUserId = "u1";

    private static (IUserDashboardPreferenceService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IUserDashboardPreferenceService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNoneExists_CreatesDefaultWithThresholdTen()
    {
        var (sut, db) = BuildService(nameof(GetOrCreateAsync_WhenNoneExists_CreatesDefaultWithThresholdTen));

        var result = await sut.GetOrCreateAsync();

        result.InboxWarningThreshold.Should().Be(10);
        (await db.DashboardPreferences.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCalledTwice_DoesNotDuplicateTheRecord()
    {
        var (sut, db) = BuildService(nameof(GetOrCreateAsync_WhenCalledTwice_DoesNotDuplicateTheRecord));

        var first = await sut.GetOrCreateAsync();
        var second = await sut.GetOrCreateAsync();

        second.Id.Should().Be(first.Id);
        (await db.DashboardPreferences.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesSeparateRecordsPerUser()
    {
        var dbName = nameof(GetOrCreateAsync_CreatesSeparateRecordsPerUser);
        var (userA, db) = BuildService(dbName, "user-a");
        var (userB, _) = BuildService(dbName, "user-b");

        var prefsA = await userA.GetOrCreateAsync();
        var prefsB = await userB.GetOrCreateAsync();

        prefsA.Id.Should().NotBe(prefsB.Id);
        (await db.DashboardPreferences.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task UpdateAsync_PersistsLayoutAndThreshold()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_PersistsLayoutAndThreshold));
        await sut.GetOrCreateAsync();

        var result = await sut.UpdateAsync(new UpdateDashboardPreferenceDto(
            WidgetOrder: "[\"inbox\",\"today\"]",
            CollapsedWidgets: "[\"goals\"]",
            InboxWarningThreshold: 25));

        result.InboxWarningThreshold.Should().Be(25);
        var stored = await db.DashboardPreferences.AsNoTracking().SingleAsync();
        stored.WidgetOrder.Should().Be("[\"inbox\",\"today\"]");
        stored.CollapsedWidgets.Should().Be("[\"goals\"]");
    }

    [Fact]
    public async Task UpdateAsync_WhenNoRecordExists_CreatesOne()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_WhenNoRecordExists_CreatesOne));

        await sut.UpdateAsync(new UpdateDashboardPreferenceDto(null, null, InboxWarningThreshold: 5));

        var stored = await db.DashboardPreferences.AsNoTracking().SingleAsync();
        stored.UserId.Should().Be(DefaultUserId);
        stored.InboxWarningThreshold.Should().Be(5);
    }

    [Fact]
    public async Task UpdateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var (sut, _) = BuildService(nameof(UpdateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNoneExists_DefaultsToStarterModeOnAndOnboardingNotStarted()
    {
        var (sut, _) = BuildService(nameof(GetOrCreateAsync_WhenNoneExists_DefaultsToStarterModeOnAndOnboardingNotStarted));

        var result = await sut.GetOrCreateAsync();

        result.StarterModeEnabled.Should().BeTrue();
        result.OnboardingCompleted.Should().BeFalse();
        result.OnboardingDismissed.Should().BeFalse();
        result.OnboardingStep.Should().Be(0);
    }

    [Fact]
    public async Task SetStarterModeAsync_PersistsValueAndCreatesRecordIfMissing()
    {
        var (sut, db) = BuildService(nameof(SetStarterModeAsync_PersistsValueAndCreatesRecordIfMissing));

        var result = await sut.SetStarterModeAsync(false);

        result.StarterModeEnabled.Should().BeFalse();
        var stored = await db.DashboardPreferences.AsNoTracking().SingleAsync();
        stored.StarterModeEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetOnboardingStepAsync_PersistsStepAndClampsNegativeValues()
    {
        var (sut, db) = BuildService(nameof(SetOnboardingStepAsync_PersistsStepAndClampsNegativeValues));

        var result = await sut.SetOnboardingStepAsync(3);
        result.OnboardingStep.Should().Be(3);

        var clamped = await sut.SetOnboardingStepAsync(-5);
        clamped.OnboardingStep.Should().Be(0);

        var stored = await db.DashboardPreferences.AsNoTracking().SingleAsync();
        stored.OnboardingStep.Should().Be(0);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_SetsOnboardingCompletedTrue()
    {
        var (sut, db) = BuildService(nameof(CompleteOnboardingAsync_SetsOnboardingCompletedTrue));

        var result = await sut.CompleteOnboardingAsync();

        result.OnboardingCompleted.Should().BeTrue();
        (await db.DashboardPreferences.AsNoTracking().SingleAsync()).OnboardingCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task DismissOnboardingAsync_SetsOnboardingDismissedTrue()
    {
        var (sut, db) = BuildService(nameof(DismissOnboardingAsync_SetsOnboardingDismissedTrue));

        var result = await sut.DismissOnboardingAsync();

        result.OnboardingDismissed.Should().BeTrue();
        (await db.DashboardPreferences.AsNoTracking().SingleAsync()).OnboardingDismissed.Should().BeTrue();
    }

    [Fact]
    public async Task ResetOnboardingAsync_ClearsCompletedDismissedAndStep()
    {
        var (sut, db) = BuildService(nameof(ResetOnboardingAsync_ClearsCompletedDismissedAndStep));
        await sut.SetOnboardingStepAsync(4);
        await sut.CompleteOnboardingAsync();

        var result = await sut.ResetOnboardingAsync();

        result.OnboardingCompleted.Should().BeFalse();
        result.OnboardingDismissed.Should().BeFalse();
        result.OnboardingStep.Should().Be(0);
        var stored = await db.DashboardPreferences.AsNoTracking().SingleAsync();
        stored.OnboardingCompleted.Should().BeFalse();
        stored.OnboardingStep.Should().Be(0);
    }
}
