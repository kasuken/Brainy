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
/// Unit tests for <see cref="ITodayService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class TodayServiceTests
{
    private const string DefaultUserId = "u1";

    // Deterministic clock: the service resolves "today" from this anchor, so tests
    // never race the real calendar (midnight/time-zone boundaries).
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    private static (ITodayService sut, BrainyDbContext db) BuildService(string dbName, string userId = DefaultUserId)
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
        return (sp.GetRequiredService<ITodayService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Project CreateProject(
        string userId,
        bool isArchived = false,
        ProjectPriority priority = ProjectPriority.Medium)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Project",
            Status = ProjectStatus.Active,
            Priority = priority,
            IsArchived = isArchived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static TaskItem CreateTask(
        Guid projectId,
        string userId,
        DateTime? dueDate = null,
        TaskItemStatus status = TaskItemStatus.Todo,
        TaskPriority priority = TaskPriority.Medium,
        bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Task",
            Status = status,
            Priority = priority,
            DueDate = dueDate,
            IsArchived = isArchived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    // ── GetOverdueAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverdueAsync_WithOverdueTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WithOverdueTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetOverdueAsync_WithFutureTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WithFutureTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1), isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueAsync_WithArchivedProject_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WithArchivedProject_ExcludesIt));
        var project = CreateProject(DefaultUserId, isArchived: true);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WithDoneTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1), status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetDueTodayAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueTodayAsync_WithTaskDueToday_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueTodayAsync_WithTaskDueToday_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueTodayAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetDueTodayAsync_WithYesterdayTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueTodayAsync_WithYesterdayTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueTodayAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueTodayAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueTodayAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueTodayAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetDueThisWeekAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDueThisWeekAsync_WithTaskDueTomorrow_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueThisWeekAsync_WithTaskDueTomorrow_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueThisWeekAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetDueThisWeekAsync_WithTaskDueToday_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueThisWeekAsync_WithTaskDueToday_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueThisWeekAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueThisWeekAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueThisWeekAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(2), isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueThisWeekAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueThisWeekAsync_WithTaskDueNextMonday_ExcludesItFromCurrentWeek()
    {
        var (sut, db) = BuildService(nameof(GetDueThisWeekAsync_WithTaskDueNextMonday_ExcludesItFromCurrentWeek));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(7));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.GetDueThisWeekAsync();

        result.Should().BeEmpty();
    }

    // ── GetCurrentTasksAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentTasksAsync_WithInProgressTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetCurrentTasksAsync_WithInProgressTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCurrentTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetCurrentTasksAsync_ExcludesOtherUsers()
    {
        // Arrange
        var dbName = nameof(GetCurrentTasksAsync_ExcludesOtherUsers);
        var (sut, db) = BuildService(dbName, "u1");
        var project = CreateProject("other-user");
        var task = CreateTask(project.Id, "other-user", status: TaskItemStatus.InProgress);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetCurrentTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetInboxCountAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetInboxCountAsync_WithInboxNotes_ReturnsCorrectCount()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetInboxCountAsync_WithInboxNotes_ReturnsCorrectCount));
        db.Notes.AddRange(
            new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Inbox 1", Content = "", Status = NoteStatus.Inbox, ParaCategory = ParaCategory.Project, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Inbox 2", Content = "", Status = NoteStatus.Inbox, ParaCategory = ParaCategory.Area, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Active",  Content = "", Status = NoteStatus.Active, ParaCategory = ParaCategory.Resource, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetInboxCountAsync();

        // Assert
        result.Should().Be(2);
    }

    // ── Archive exclusion coverage ────────────────────────────────────────────

    [Fact]
    public async Task GetHighPriorityTasksAsync_ExcludesArchivedTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_ExcludesArchivedTasks));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Critical, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHighPriorityTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNextTasksAsync_ExcludesArchivedProjects()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetNextTasksAsync_ExcludesArchivedProjects));
        // Due in 8 days falls in the 7–21 day "next" window
        var project = CreateProject(DefaultUserId, isArchived: true);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(8));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetNextTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── Parent surfacing from subtask due dates ───────────────────────────────

    [Fact]
    public async Task GetOverdueAsync_WhenSubtaskOverdue_SurfacesParent()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueAsync_WhenSubtaskOverdue_SurfacesParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var subtask = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        subtask.ParentTaskId = parent.Id;
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueAsync();

        // Assert
        result.Should().ContainSingle().Which.Id.Should().Be(parent.Id);
    }

    [Fact]
    public async Task GetDueTodayAsync_WhenSubtaskDueToday_SurfacesParent()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueTodayAsync_WhenSubtaskDueToday_SurfacesParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var subtask = CreateTask(project.Id, DefaultUserId, dueDate: Today);
        subtask.ParentTaskId = parent.Id;
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueTodayAsync();

        // Assert
        result.Should().ContainSingle().Which.Id.Should().Be(parent.Id);
    }

    [Fact]
    public async Task GetDueTodayAsync_WhenSubtaskDoneAndDueToday_DoesNotSurfaceParent()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetDueTodayAsync_WhenSubtaskDoneAndDueToday_DoesNotSurfaceParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var subtask = CreateTask(project.Id, DefaultUserId, dueDate: Today, status: TaskItemStatus.Done);
        subtask.ParentTaskId = parent.Id;
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetDueTodayAsync();

        // Assert
        result.Should().BeEmpty();
    }
}
