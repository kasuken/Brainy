using Brainy.Application.DTOs.Today;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ITodayNotificationService"/> resolved via the real DI container
/// with an EF Core InMemory database and a pinned clock (the service composes the
/// due-date logic of <see cref="ITodayService"/>).
/// </summary>
public class TodayNotificationServiceTests
{
    private const string DefaultUserId = "u1";

    // Deterministic clock: due-date evaluation resolves "today" from this anchor.
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    private static (ITodayNotificationService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ITodayNotificationService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "P", Status = ProjectStatus.Active };

    private static TaskItem CreateTask(string userId, Guid projectId, DateTime? dueDate)
        => new() { Id = Guid.NewGuid(), UserId = userId, ProjectId = projectId, Title = "T", DueDate = dueDate };

    private static Note CreateInboxNote(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = "N", Status = NoteStatus.Inbox };

    [Fact]
    public async Task GetNotificationsAsync_WithNoData_ReturnsEmpty()
    {
        var (sut, _) = BuildService(nameof(GetNotificationsAsync_WithNoData_ReturnsEmpty));

        var result = await sut.GetNotificationsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNotificationsAsync_WithOverdueTask_EmitsOverdueNotification()
    {
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WithOverdueTask_EmitsOverdueNotification));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().ContainSingle(n => n.Kind == TodayNotificationKind.OverdueTasks)
            .Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetNotificationsAsync_WithTaskDueToday_EmitsDueTodayNotification()
    {
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WithTaskDueToday_EmitsDueTodayNotification));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, Today));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().ContainSingle(n => n.Kind == TodayNotificationKind.DueToday)
            .Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetNotificationsAsync_WithFourTasksDueThisWeek_EmitsUpcomingDeadlines()
    {
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WithFourTasksDueThisWeek_EmitsUpcomingDeadlines));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        for (var i = 0; i < 4; i++)
            db.Tasks.Add(CreateTask(DefaultUserId, project.Id, Today.AddDays(2)));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().ContainSingle(n => n.Kind == TodayNotificationKind.UpcomingDeadlines)
            .Which.Count.Should().Be(4);
    }

    [Fact]
    public async Task GetNotificationsAsync_WithThreeTasksDueThisWeek_DoesNotEmitUpcomingDeadlines()
    {
        // The upcoming-deadlines nudge only fires when MORE than 3 tasks are due this week.
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WithThreeTasksDueThisWeek_DoesNotEmitUpcomingDeadlines));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        for (var i = 0; i < 3; i++)
            db.Tasks.Add(CreateTask(DefaultUserId, project.Id, Today.AddDays(2)));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().NotContain(n => n.Kind == TodayNotificationKind.UpcomingDeadlines);
    }

    [Fact]
    public async Task GetNotificationsAsync_WhenInboxReachesThreshold_EmitsGrowingInbox()
    {
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WhenInboxReachesThreshold_EmitsGrowingInbox));
        for (var i = 0; i < 10; i++) // default threshold is 10
            db.Notes.Add(CreateInboxNote(DefaultUserId));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().ContainSingle(n => n.Kind == TodayNotificationKind.GrowingInbox)
            .Which.Count.Should().Be(10);
    }

    [Fact]
    public async Task GetNotificationsAsync_WhenInboxBelowThreshold_DoesNotEmitGrowingInbox()
    {
        var (sut, db) = BuildService(nameof(GetNotificationsAsync_WhenInboxBelowThreshold_DoesNotEmitGrowingInbox));
        for (var i = 0; i < 9; i++)
            db.Notes.Add(CreateInboxNote(DefaultUserId));
        await db.SaveChangesAsync();

        var result = await sut.GetNotificationsAsync();

        result.Should().NotContain(n => n.Kind == TodayNotificationKind.GrowingInbox);
    }
}
