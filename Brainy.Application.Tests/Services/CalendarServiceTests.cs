using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Integration tests for <see cref="ICalendarService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class CalendarServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId   = "u2";

    // Deterministic clock: the service resolves "today" from this anchor, so tests
    // never race the real calendar (midnight/time-zone boundaries). Anchored to a
    // Monday so the Mon-Sun "due this week" window always has days after today.
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    // ── DI builder ────────────────────────────────────────────────────────────

    private static (ICalendarService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<ICalendarService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Area CreateArea(string userId) => new()
    {
        Id            = Guid.NewGuid(),
        UserId        = userId,
        Name          = "Test Area",
        CreatedAtUtc  = DateTime.UtcNow,
        UpdatedAtUtc  = DateTime.UtcNow
    };

    private static Project CreateProject(
        string userId,
        bool isArchived   = false,
        Guid? areaId      = null) => new()
    {
        Id           = Guid.NewGuid(),
        UserId       = userId,
        Name         = "Test Project",
        Status       = ProjectStatus.Active,
        Priority     = ProjectPriority.Medium,
        IsArchived   = isArchived,
        AreaId       = areaId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static TaskItem CreateTask(
        string userId,
        Guid projectId,
        DateTime? dueDate     = null,
        bool isArchived       = false,
        TaskItemStatus status  = TaskItemStatus.Todo,
        TaskPriority priority  = TaskPriority.Medium,
        string title           = "Task",
        TaskComplexity? complexity = null) => new()
    {
        Id           = Guid.NewGuid(),
        UserId       = userId,
        ProjectId    = projectId,
        Title        = title,
        Status       = status,
        Priority     = priority,
        Complexity   = complexity,
        DueDate      = dueDate,
        IsArchived   = isArchived,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    // ── GetCalendarTasksAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCalendarTasks_ReturnsOnlyTasksWithDueDate()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_ReturnsOnlyTasksWithDueDate));
        var project   = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: Today));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: null));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetCalendarTasks_ExcludesArchivedTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_ExcludesArchivedTasks));
        var project   = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: Today, isArchived: true));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCalendarTasks_ExcludesTasksFromArchivedProjects()
    {
        // Arrange
        var (sut, db)       = BuildService(nameof(GetCalendarTasks_ExcludesTasksFromArchivedProjects));
        var archivedProject = CreateProject(DefaultUserId, isArchived: true);
        db.Projects.Add(archivedProject);
        db.Tasks.Add(CreateTask(DefaultUserId, archivedProject.Id, dueDate: Today));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCalendarTasks_ExcludesDoneAndArchivedStatusTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_ExcludesDoneAndArchivedStatusTasks));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, status: TaskItemStatus.Done));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, status: TaskItemStatus.Archived));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCalendarTasks_FilterByProject_ReturnsMatchingOnly()
    {
        // Arrange
        var (sut, db)     = BuildService(nameof(GetCalendarTasks_FilterByProject_ReturnsMatchingOnly));
        var targetProject = CreateProject(DefaultUserId);
        var otherProject  = CreateProject(DefaultUserId);
        var today         = Today;
        db.Projects.AddRange(targetProject, otherProject);
        db.Tasks.Add(CreateTask(DefaultUserId, targetProject.Id, dueDate: today, title: "Target"));
        db.Tasks.Add(CreateTask(DefaultUserId, otherProject.Id,  dueDate: today, title: "Other"));
        await db.SaveChangesAsync();

        var filter = new CalendarFilterDto(ProjectId: targetProject.Id);

        // Act
        var result = await sut.GetCalendarTasksAsync(filter);

        // Assert
        result.Should().ContainSingle()
            .Which.ProjectId.Should().Be(targetProject.Id);
    }

    [Fact]
    public async Task GetCalendarTasks_FilterByPriority_ReturnsMatchingOnly()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_FilterByPriority_ReturnsMatchingOnly));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, priority: TaskPriority.High,   title: "High"));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, priority: TaskPriority.Medium, title: "Medium"));
        await db.SaveChangesAsync();

        var filter = new CalendarFilterDto(Priority: TaskPriority.High);

        // Act
        var result = await sut.GetCalendarTasksAsync(filter);

        // Assert
        result.Should().ContainSingle()
            .Which.Priority.Should().Be(TaskPriority.High);
    }

    [Fact]
    public async Task GetCalendarTasks_FilterBySearchTerm_ReturnsMatchingOnly()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_FilterBySearchTerm_ReturnsMatchingOnly));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, title: "Deploy release"));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, title: "Write docs"));
        await db.SaveChangesAsync();

        var filter = new CalendarFilterDto(SearchTerm: "DEPLOY");

        // Act
        var result = await sut.GetCalendarTasksAsync(filter);

        // Assert
        result.Should().ContainSingle()
            .Which.Title.Should().Be("Deploy release");
    }

    [Fact]
    public async Task GetCalendarTasks_FilterByArea_ReturnsMatchingOnly()
    {
        // Arrange
        var (sut, db)     = BuildService(nameof(GetCalendarTasks_FilterByArea_ReturnsMatchingOnly));
        var area          = CreateArea(DefaultUserId);
        var projectInArea = CreateProject(DefaultUserId, areaId: area.Id);
        var projectNoArea = CreateProject(DefaultUserId);
        var today         = Today;
        db.Areas.Add(area);
        db.Projects.AddRange(projectInArea, projectNoArea);
        db.Tasks.Add(CreateTask(DefaultUserId, projectInArea.Id, dueDate: today, title: "In area"));
        db.Tasks.Add(CreateTask(DefaultUserId, projectNoArea.Id, dueDate: today, title: "No area"));
        await db.SaveChangesAsync();

        var filter = new CalendarFilterDto(AreaId: area.Id);

        // Act
        var result = await sut.GetCalendarTasksAsync(filter);

        // Assert
        result.Should().ContainSingle()
            .Which.AreaId.Should().Be(area.Id);
    }

    [Fact]
    public async Task GetCalendarTasks_SetsIsOverdue_WhenDueDateIsInPast()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_SetsIsOverdue_WhenDueDateIsInPast));
        var project   = CreateProject(DefaultUserId);
        var yesterday = Today.AddDays(-1);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: yesterday));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarTasks_DoesNotSetIsOverdue_WhenDueDateIsFuture()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCalendarTasks_DoesNotSetIsOverdue_WhenDueDateIsFuture));
        var project   = CreateProject(DefaultUserId);
        var tomorrow  = Today.AddDays(1);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: tomorrow));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task GetCalendarTasks_ProjectsComplexityEstimate()
    {
        var (sut, db) = BuildService(nameof(GetCalendarTasks_ProjectsComplexityEstimate));
        var project   = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: Today, complexity: TaskComplexity.XL));
        await db.SaveChangesAsync();

        var result = await sut.GetCalendarTasksAsync();

        result.Should().ContainSingle()
            .Which.Complexity.Should().Be(TaskComplexity.XL);
    }

    [Fact]
    public async Task GetCalendarTasks_ProjectsBlockedDependencyCounts()
    {
        var (sut, db) = BuildService(nameof(GetCalendarTasks_ProjectsBlockedDependencyCounts));
        var project = CreateProject(DefaultUserId);
        var completedPrerequisite = CreateTask(DefaultUserId, project.Id, status: TaskItemStatus.Done, title: "Completed prerequisite");
        var openPrerequisite = CreateTask(DefaultUserId, project.Id, title: "Open prerequisite");
        var blockedTask = CreateTask(DefaultUserId, project.Id, dueDate: Today, title: "Blocked task");

        db.Projects.Add(project);
        db.Tasks.AddRange(completedPrerequisite, openPrerequisite, blockedTask);
        db.TaskDependencies.AddRange(
            new TaskDependency
            {
                Id = Guid.NewGuid(),
                TaskId = blockedTask.Id,
                DependsOnTaskId = completedPrerequisite.Id
            },
            new TaskDependency
            {
                Id = Guid.NewGuid(),
                TaskId = blockedTask.Id,
                DependsOnTaskId = openPrerequisite.Id
            });
        await db.SaveChangesAsync();

        var result = await sut.GetCalendarTasksAsync();

        result.Should().ContainSingle(task => task.Id == blockedTask.Id)
            .Which.Should().Match<CalendarTaskDto>(task =>
                task.DependencyCount == 2 &&
                task.UnresolvedDependencyCount == 1 &&
                task.IsBlocked);
    }

    [Fact]
    public async Task GetCalendarTasks_ExcludesOtherUsersData()
    {
        // Arrange
        var (sut, db)    = BuildService(nameof(GetCalendarTasks_ExcludesOtherUsersData));
        var ownProject   = CreateProject(DefaultUserId);
        var otherProject = CreateProject(OtherUserId);
        var today        = Today;
        db.Projects.AddRange(ownProject, otherProject);
        db.Tasks.Add(CreateTask(DefaultUserId, ownProject.Id,   dueDate: today, title: "Mine"));
        db.Tasks.Add(CreateTask(OtherUserId,   otherProject.Id, dueDate: today, title: "Others"));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCalendarTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Title.Should().Be("Mine");
    }

    // ── GetUpcomingDeadlinesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingDeadlines_GroupsTasksCorrectly()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetUpcomingDeadlines_GroupsTasksCorrectly));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;

        // Compute the Sunday of the current Mon–Sun week (mirrors the service logic)
        var dayOfWeek    = (int)today.DayOfWeek;
        var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek;
        var weekEnd      = today.AddDays(mondayOffset).AddDays(6); // Sunday inclusive

        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today,             title: "DueToday"));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today.AddDays(-1), title: "Overdue"));

        // Only seed a DueThisWeek task when there is at least one day between today and weekEnd
        if (weekEnd > today)
            db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: weekEnd, title: "DueThisWeek"));

        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetUpcomingDeadlinesAsync();

        // Assert
        result.DueToday.Should().ContainSingle(t => t.Title == "DueToday");
        result.Overdue.Should().ContainSingle(t => t.Title == "Overdue");

        if (weekEnd > today)
            result.DueThisWeek.Should().ContainSingle(t => t.Title == "DueThisWeek");
        else
            result.DueThisWeek.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUpcomingDeadlines_ProjectsBlockedDependencyCounts()
    {
        var (sut, db) = BuildService(nameof(GetUpcomingDeadlines_ProjectsBlockedDependencyCounts));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(DefaultUserId, project.Id, title: "Open prerequisite");
        var blockedTask = CreateTask(DefaultUserId, project.Id, dueDate: Today, title: "Blocked today");

        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, blockedTask);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = blockedTask.Id,
            DependsOnTaskId = prerequisite.Id
        });
        await db.SaveChangesAsync();

        var result = await sut.GetUpcomingDeadlinesAsync();

        result.DueToday.Should().ContainSingle(task => task.Id == blockedTask.Id)
            .Which.Should().Match<CalendarTaskDto>(task =>
                task.DependencyCount == 1 &&
                task.UnresolvedDependencyCount == 1 &&
                task.IsBlocked);
    }

    [Fact]
    public async Task GetUpcomingDeadlines_ExcludesArchivedTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetUpcomingDeadlines_ExcludesArchivedTasks));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today,             isArchived: true, title: "ArchivedToday"));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today.AddDays(-1), isArchived: true, title: "ArchivedOverdue"));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetUpcomingDeadlinesAsync();

        // Assert
        result.DueToday.Should().BeEmpty();
        result.DueThisWeek.Should().BeEmpty();
        result.Overdue.Should().BeEmpty();
    }

    // ── RescheduleDueDateAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RescheduleDueDate_UpdatesTaskDueDate()
    {
        // Arrange
        var (sut, db)  = BuildService(nameof(RescheduleDueDate_UpdatesTaskDueDate));
        var project    = CreateProject(DefaultUserId);
        var task       = CreateTask(DefaultUserId, project.Id, dueDate: Today);
        var newDueDate = Today.AddDays(7);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        await sut.RescheduleDueDateAsync(task.Id, newDueDate);

        // Assert
        var updated = await db.Tasks.FindAsync(task.Id);
        updated!.DueDate.Should().Be(newDueDate.Date);
    }

    [Fact]
    public async Task RescheduleDueDate_ThrowsKeyNotFoundException_WhenTaskNotFound()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(RescheduleDueDate_ThrowsKeyNotFoundException_WhenTaskNotFound));
        var unknownId = Guid.NewGuid();

        // Act
        var act = () => sut.RescheduleDueDateAsync(unknownId, Today.AddDays(1));

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RescheduleDueDate_ThrowsKeyNotFoundException_WhenTaskBelongsToOtherUser()
    {
        // Arrange
        var (sut, db)  = BuildService(nameof(RescheduleDueDate_ThrowsKeyNotFoundException_WhenTaskBelongsToOtherUser));
        var project    = CreateProject(OtherUserId);
        var otherTask  = CreateTask(OtherUserId, project.Id, dueDate: Today);
        db.Projects.Add(project);
        db.Tasks.Add(otherTask);
        await db.SaveChangesAsync();

        // Act — sut is scoped to DefaultUserId, not OtherUserId
        var act = () => sut.RescheduleDueDateAsync(otherTask.Id, Today.AddDays(1));

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── GetWorkloadAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkload_ReturnsCountsPerDate()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetWorkload_ReturnsCountsPerDate));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today.AddDays(1)));
        await db.SaveChangesAsync();

        var from = DateOnly.FromDateTime(today);
        var to   = DateOnly.FromDateTime(today.AddDays(1));

        // Act
        var result = await sut.GetWorkloadAsync(from, to);

        // Assert
        result[DateOnly.FromDateTime(today)].Should().Be(2);
        result[DateOnly.FromDateTime(today.AddDays(1))].Should().Be(1);
    }

    [Fact]
    public async Task GetWorkload_ExcludesArchivedTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetWorkload_ExcludesArchivedTasks));
        var project   = CreateProject(DefaultUserId);
        var today     = Today;
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: today, isArchived: true));
        await db.SaveChangesAsync();

        var dateOnly = DateOnly.FromDateTime(today);

        // Act
        var result = await sut.GetWorkloadAsync(dateOnly, dateOnly);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorkload_ExcludesTasksOutsideRange()
    {
        // Arrange
        var (sut, db)   = BuildService(nameof(GetWorkload_ExcludesTasksOutsideRange));
        var project     = CreateProject(DefaultUserId);
        var today       = Today;
        var insideDate  = today.AddDays(1);
        var outsideDate = today.AddDays(5);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: insideDate));
        db.Tasks.Add(CreateTask(DefaultUserId, project.Id, dueDate: outsideDate));
        await db.SaveChangesAsync();

        var from = DateOnly.FromDateTime(insideDate);
        var to   = DateOnly.FromDateTime(insideDate.AddDays(1)); // range does NOT include outsideDate

        // Act
        var result = await sut.GetWorkloadAsync(from, to);

        // Assert
        result.Should().ContainKey(DateOnly.FromDateTime(insideDate));
        result.Should().NotContainKey(DateOnly.FromDateTime(outsideDate));
    }
}
