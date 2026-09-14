using AwesomeAssertions;
using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Covers <see cref="IPushDispatchService"/>: reuse of <see cref="ITodayNotificationService"/>
/// (no note/task content ever reaches a payload), quiet hours, per-category frequency
/// capping, per-category opt-out, and pruning of expired subscriptions — issue #315's
/// guardrails.
/// </summary>
public class PushDispatchServiceTests
{
    private const string UserA = "dispatch-user-a";
    private const string UserB = "dispatch-user-b";

    // 2026-06-14 is a Sunday. 18:00 UTC is outside the default quiet-hours window
    // (21:00-08:00) and at/after both the default daily-nudge hour (8) and the default
    // weekly-review hour (17), so both time-gated categories are eligible at this instant.
    private static readonly DateTimeOffset SundayEvening = new(2026, 6, 14, 18, 0, 0, TimeSpan.Zero);

    private static (IPushDispatchService sut, BrainyDbContext db, FakePushNotificationSender sender) BuildService(
        string dbName,
        DateTimeOffset? now = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddScoped<IBackgroundUserContextAccessor, FakeBackgroundUserContextAccessor>();
        services.AddScoped<ICurrentUserService, AmbientCurrentUserService>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now ?? SundayEvening));

        var sender = new FakePushNotificationSender();
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IPushDispatchService>(), sp.GetRequiredService<BrainyDbContext>(), sender);
    }

    private static Project CreateProject(string userId) =>
        new() { Id = Guid.NewGuid(), UserId = userId, Name = "P", Status = ProjectStatus.Active };

    private static TaskItem CreateTask(string userId, Guid projectId, string title, DateTime? dueDate) =>
        new() { Id = Guid.NewGuid(), UserId = userId, ProjectId = projectId, Title = title, DueDate = dueDate };

    private static PushSubscription CreateSubscription(string userId, string endpoint) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Endpoint = endpoint,
            EndpointHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint))),
            P256dh = "p256dh",
            Auth = "auth",
        };

    private static PushNotificationPreference EnabledPreference(string userId) =>
        new() { UserId = userId, Enabled = true };

    [Fact]
    public async Task DispatchDueNotificationsAsync_WithNoEnabledUsers_SendsNothing()
    {
        var (sut, _, sender) = BuildService(nameof(DispatchDueNotificationsAsync_WithNoEnabledUsers_SendsNothing));

        var result = await sut.DispatchDueNotificationsAsync();

        result.UsersEvaluated.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_WithOverdueTask_SendsCountOnly_NeverTheTaskTitle()
    {
        var (sut, db, sender) = BuildService(nameof(DispatchDueNotificationsAsync_WithOverdueTask_SendsCountOnly_NeverTheTaskTitle));
        const string secretTitle = "Confidential Q3 layoffs memo";

        var project = CreateProject(UserA);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, secretTitle, SundayEvening.UtcDateTime.Date.AddDays(-2)));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.NotificationsSent.Should().Be(1);
        sender.Sent.Should().ContainSingle();
        var payload = sender.Sent[0].Payload;
        payload.Category.Should().Be(PushNotificationCategory.OverdueTask);
        payload.Heading.Should().NotContain(secretTitle);
        payload.Body.Should().NotContain(secretTitle, "a push payload must never carry note or task content");
        payload.Body.Should().Contain("1", "the payload may report counts");
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_CalledTwiceSameDay_SendsOverdueOnlyOnce()
    {
        var (sut, db, sender) = BuildService(nameof(DispatchDueNotificationsAsync_CalledTwiceSameDay_SendsOverdueOnlyOnce));
        var project = CreateProject(UserA);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, "T", SundayEvening.UtcDateTime.Date.AddDays(-1)));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        await db.SaveChangesAsync();

        await sut.DispatchDueNotificationsAsync();
        var second = await sut.DispatchDueNotificationsAsync();

        second.NotificationsSent.Should().Be(0, "the overdue-task category is capped to once per local calendar day");
        sender.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_DuringQuietHours_SendsNothing()
    {
        // 22:00 UTC on the same Sunday falls inside the default 21:00-08:00 quiet-hours window.
        var quietHourNow = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);
        var (sut, db, sender) = BuildService(
            nameof(DispatchDueNotificationsAsync_DuringQuietHours_SendsNothing), quietHourNow);

        var project = CreateProject(UserA);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, "T", quietHourNow.UtcDateTime.Date.AddDays(-1)));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushNotificationPreferences.Add(EnabledPreference(UserA));
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.NotificationsSent.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_WithCategoryDisabled_DoesNotSendThatCategory()
    {
        var (sut, db, sender) = BuildService(nameof(DispatchDueNotificationsAsync_WithCategoryDisabled_DoesNotSendThatCategory));
        var project = CreateProject(UserA);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, "T", SundayEvening.UtcDateTime.Date.AddDays(-1)));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            OverdueTaskEnabled = false,
            DailyFocusNudgeEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.NotificationsSent.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_WeeklyReviewReminder_FiresOnConfiguredDayAndHour()
    {
        var (sut, db, sender) = BuildService(nameof(DispatchDueNotificationsAsync_WeeklyReviewReminder_FiresOnConfiguredDayAndHour));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            OverdueTaskEnabled = false,
            WeeklyReviewReminderEnabled = true,
            WeeklyReviewDayOfWeek = DayOfWeek.Sunday,
            WeeklyReviewHour = 17,
        });
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.NotificationsSent.Should().Be(1);
        sender.Sent.Should().ContainSingle().Which.Payload.Category.Should().Be(PushNotificationCategory.WeeklyReviewReminder);
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_WhenPushServiceReportsExpired_PrunesSubscription_DoesNotRetry()
    {
        var (sut, db, sender) = BuildService(nameof(DispatchDueNotificationsAsync_WhenPushServiceReportsExpired_PrunesSubscription_DoesNotRetry));
        sender.OutcomeSelector = (_, _) => PushSendResult.Expired(410);

        var project = CreateProject(UserA);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, "T", SundayEvening.UtcDateTime.Date.AddDays(-1)));
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/gone"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.SubscriptionsPruned.Should().Be(1);
        (await db.PushSubscriptions.AsNoTracking().AnyAsync()).Should().BeFalse();

        // A second run must not resend to the pruned subscription and must not throw for
        // having no subscriptions left.
        sender.Sent.Clear();
        var second = await sut.DispatchDueNotificationsAsync();
        second.NotificationsSent.Should().Be(0);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchDueNotificationsAsync_EvaluatesMultipleUsersIndependently()
    {
        const string dbName = nameof(DispatchDueNotificationsAsync_EvaluatesMultipleUsersIndependently);
        var (sut, db, sender) = BuildService(dbName);

        var projectA = CreateProject(UserA);
        var projectB = CreateProject(UserB);
        db.Projects.AddRange(projectA, projectB);
        db.Tasks.Add(CreateTask(UserA, projectA.Id, "A's task", SundayEvening.UtcDateTime.Date.AddDays(-1)));
        // User B has no overdue task and both other categories off, so nothing is due for B.
        db.PushSubscriptions.Add(CreateSubscription(UserA, "https://push.example.com/a"));
        db.PushSubscriptions.Add(CreateSubscription(UserB, "https://push.example.com/b"));
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserA,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        db.PushNotificationPreferences.Add(new PushNotificationPreference
        {
            UserId = UserB,
            Enabled = true,
            DailyFocusNudgeEnabled = false,
            OverdueTaskEnabled = false,
            WeeklyReviewReminderEnabled = false,
        });
        await db.SaveChangesAsync();

        var result = await sut.DispatchDueNotificationsAsync();

        result.UsersEvaluated.Should().Be(2);
        result.NotificationsSent.Should().Be(1);
        sender.Sent.Should().ContainSingle().Which.Subscription.Endpoint.Should().Be("https://push.example.com/a");
    }
}
