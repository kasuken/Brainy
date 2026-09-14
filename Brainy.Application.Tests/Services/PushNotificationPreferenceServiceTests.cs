using AwesomeAssertions;
using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Covers <see cref="IPushNotificationPreferenceService"/>: strictly opt-in/off-by-default
/// (issue #315), lazy row creation, and per-user isolation.
/// </summary>
public class PushNotificationPreferenceServiceTests
{
    private const string UserA = "push-pref-user-a";
    private const string UserB = "push-pref-user-b";

    private static (IPushNotificationPreferenceService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = UserA)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        services.AddBrainyApplication();
        services.AddSingleton<IPushNotificationSender, FakePushNotificationSender>();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IPushNotificationPreferenceService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task GetOrCreateAsync_WithNoRow_ReturnsDisabledDefaults_AndWritesNothing()
    {
        var (sut, db) = BuildService(nameof(GetOrCreateAsync_WithNoRow_ReturnsDisabledDefaults_AndWritesNothing));

        var preference = await sut.GetOrCreateAsync();

        preference.Enabled.Should().BeFalse("push is strictly opt-in and off by default");
        preference.OverdueTaskEnabled.Should().BeTrue();
        preference.DailyFocusNudgeEnabled.Should().BeTrue();
        preference.WeeklyReviewReminderEnabled.Should().BeTrue();
        preference.QuietHoursEnabled.Should().BeTrue();
        (await db.PushNotificationPreferences.AnyAsync()).Should().BeFalse(
            "merely reading settings must never opt a user in or create a row");
    }

    [Fact]
    public async Task UpdateAsync_PersistsAllFields_AndOneClickDisableStopsEverything()
    {
        var (sut, _) = BuildService(nameof(UpdateAsync_PersistsAllFields_AndOneClickDisableStopsEverything));

        var enabled = await sut.UpdateAsync(new UpdatePushNotificationPreferenceDto(
            Enabled: true,
            DailyFocusNudgeEnabled: true,
            OverdueTaskEnabled: true,
            WeeklyReviewReminderEnabled: false,
            QuietHoursEnabled: false,
            QuietHoursStart: new TimeOnly(22, 0),
            QuietHoursEnd: new TimeOnly(6, 0),
            DailyFocusNudgeHour: 9,
            WeeklyReviewDayOfWeek: DayOfWeek.Monday,
            WeeklyReviewHour: 18));

        enabled.Enabled.Should().BeTrue();
        enabled.WeeklyReviewReminderEnabled.Should().BeFalse();
        enabled.QuietHoursEnabled.Should().BeFalse();
        enabled.DailyFocusNudgeHour.Should().Be(9);
        enabled.WeeklyReviewDayOfWeek.Should().Be(DayOfWeek.Monday);

        // The one-click "off" path: master switch false must persist regardless of the
        // per-category flags, and take effect immediately for the next dispatch.
        var disabled = await sut.UpdateAsync(new UpdatePushNotificationPreferenceDto(
            Enabled: false,
            DailyFocusNudgeEnabled: true,
            OverdueTaskEnabled: true,
            WeeklyReviewReminderEnabled: true,
            QuietHoursEnabled: true,
            QuietHoursStart: new TimeOnly(21, 0),
            QuietHoursEnd: new TimeOnly(8, 0),
            DailyFocusNudgeHour: 8,
            WeeklyReviewDayOfWeek: DayOfWeek.Sunday,
            WeeklyReviewHour: 17));

        disabled.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public async Task UpdateAsync_WithInvalidDailyFocusNudgeHour_Throws(int hour)
    {
        var (sut, _) = BuildService(nameof(UpdateAsync_WithInvalidDailyFocusNudgeHour_Throws) + hour);

        var act = () => sut.UpdateAsync(new UpdatePushNotificationPreferenceDto(
            true, true, true, true, true, new TimeOnly(21, 0), new TimeOnly(8, 0), hour, DayOfWeek.Sunday, 17));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Preferences_AreIsolatedPerUser()
    {
        const string dbName = nameof(Preferences_AreIsolatedPerUser);
        var (sutA, _) = BuildService(dbName, UserA);
        var (sutB, _) = BuildService(dbName, UserB);

        await sutA.UpdateAsync(new UpdatePushNotificationPreferenceDto(
            true, true, true, true, true, new TimeOnly(21, 0), new TimeOnly(8, 0), 8, DayOfWeek.Sunday, 17));

        var bPreference = await sutB.GetOrCreateAsync();

        bPreference.Enabled.Should().BeFalse("user B never opted in, regardless of user A's setting");
    }
}
