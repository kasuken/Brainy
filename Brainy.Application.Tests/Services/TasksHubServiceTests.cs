using Brainy.Application.DTOs.Tasks;
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
/// Integration tests for <see cref="ITasksHubService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class TasksHubServiceTests
{
    private const string DefaultUserId = "u1";

    // Deterministic clock: the service resolves "today" from this anchor, so tests
    // never race the real calendar (midnight/time-zone boundaries).
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = FixedNow.UtcDateTime.Date;

    private static (ITasksHubService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(clock ?? new FixedTimeProvider(FixedNow));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ITasksHubService>(), sp.GetRequiredService<BrainyDbContext>());
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
        bool isArchived = false,
        DateTime? updatedAt = null,
        Guid? parentTaskId = null)
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
            ParentTaskId = parentTaskId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = updatedAt ?? DateTime.UtcNow
        };

    // ── GetActiveTasksAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveTasksAsync_WithNonArchivedNonDoneTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithNonArchivedNonDoneTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithArchivedProject_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithArchivedProject_ExcludesIt));
        var project = CreateProject(DefaultUserId, isArchived: true);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithDoneTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithArchivedStatusTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithArchivedStatusTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Archived);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithSubtask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithSubtask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var parentTask = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parentTask.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parentTask, subtask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(parentTask.Id);
    }

    [Fact]
    public async Task GetActiveTasksAsync_WithOtherUsersTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetActiveTasksAsync_WithOtherUsersTask_ExcludesIt));
        var project = CreateProject("other-user");
        var task = CreateTask(project.Id, "other-user");
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetActiveTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetHighPriorityTasksAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetHighPriorityTasksAsync_WithHighPriorityTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_WithHighPriorityTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.High);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHighPriorityTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetHighPriorityTasksAsync_WithCriticalPriorityTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_WithCriticalPriorityTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Critical);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHighPriorityTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetHighPriorityTasksAsync_WithMediumPriorityTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_WithMediumPriorityTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Medium);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHighPriorityTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHighPriorityTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_WithArchivedTask_ExcludesIt));
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
    public async Task GetHighPriorityTasksAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHighPriorityTasksAsync_WithDoneTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.High, status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHighPriorityTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetOnHoldTasksAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetOnHoldTasksAsync_WithWaitingStatusTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOnHoldTasksAsync_WithWaitingStatusTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Waiting);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOnHoldTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetOnHoldTasksAsync_WithInProgressTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOnHoldTasksAsync_WithInProgressTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOnHoldTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnHoldTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOnHoldTasksAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Waiting, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOnHoldTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetOverdueTasksAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetOverdueTasksAsync_WithYesterdayDueDate_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_WithYesterdayDueDate_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetOverdueTasksAsync_WithTodayDueDate_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_WithTodayDueDate_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueTasksAsync_WithFutureDueDate_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_WithFutureDueDate_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueTasksAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_WithDoneTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1), status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1), isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueTasksAsync_OrderedByDueDateAscending()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetOverdueTasksAsync_OrderedByDueDateAscending));
        var project = CreateProject(DefaultUserId);
        var older = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-5));
        var newer = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.AddRange(older, newer);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetOverdueTasksAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(older.Id);
        result[1].Id.Should().Be(newer.Id);
    }

    // ── GetTasksNeedingAttentionAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetTasksNeedingAttentionAsync_WithOverdueTask_IncludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksNeedingAttentionAsync_WithOverdueTask_IncludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksNeedingAttentionAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetTasksNeedingAttentionAsync_WithTaskDueToday_IncludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksNeedingAttentionAsync_WithTaskDueToday_IncludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksNeedingAttentionAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetTasksNeedingAttentionAsync_WithCriticalPriorityTask_IncludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksNeedingAttentionAsync_WithCriticalPriorityTask_IncludesIt));
        var project = CreateProject(DefaultUserId);
        // Future due date but critical priority — should still appear
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(7), priority: TaskPriority.Critical);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksNeedingAttentionAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetTasksNeedingAttentionAsync_WhenTaskIsOverdueAndCritical_NoDuplicates()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksNeedingAttentionAsync_WhenTaskIsOverdueAndCritical_NoDuplicates));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-2), priority: TaskPriority.Critical);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksNeedingAttentionAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetTasksNeedingAttentionAsync_WithDoneArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksNeedingAttentionAsync_WithDoneArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var doneTask = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(-1), status: TaskItemStatus.Done);
        var archivedTask = CreateTask(project.Id, DefaultUserId, dueDate: Today, priority: TaskPriority.Critical, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(doneTask, archivedTask);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksNeedingAttentionAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetTasksWithoutDueDateAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetTasksWithoutDueDateAsync_WithNullDueDateTask_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksWithoutDueDateAsync_WithNullDueDateTask_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: null);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksWithoutDueDateAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetTasksWithoutDueDateAsync_WithDueDateSet_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksWithoutDueDateAsync_WithDueDateSet_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(3));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksWithoutDueDateAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTasksWithoutDueDateAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetTasksWithoutDueDateAsync_WithDoneTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, dueDate: null, status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetTasksWithoutDueDateAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetStaleTasksAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetStaleTasksAsync_WithTaskNotUpdatedFor31Days_ReturnsIt()
    {
        // Arrange — persistence and application logic share the same clock. Advance it
        // after the save to exercise elapsed time without relying on the wall clock.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var (sut, db) = BuildService(
            nameof(GetStaleTasksAsync_WithTaskNotUpdatedFor31Days_ReturnsIt),
            clock: clock);
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(31));

        // Act
        var result = await sut.GetStaleTasksAsync();

        // Assert
        result.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task GetStaleTasksAsync_WithTaskUpdated29DaysAgo_ExcludesIt()
    {
        // Arrange — advance 29 days after the save: inside the freshness window.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var (sut, db) = BuildService(
            nameof(GetStaleTasksAsync_WithTaskUpdated29DaysAgo_ExcludesIt),
            clock: clock);
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(29));

        // Act
        var result = await sut.GetStaleTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStaleTasksAsync_WithDoneTask_ExcludesIt()
    {
        // Arrange — stale by clock but Done, so it must be excluded.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var (sut, db) = BuildService(
            nameof(GetStaleTasksAsync_WithDoneTask_ExcludesIt),
            clock: clock);
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(31));

        // Act
        var result = await sut.GetStaleTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStaleTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange — stale by clock but archived, so it must be excluded.
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var (sut, db) = BuildService(
            nameof(GetStaleTasksAsync_WithArchivedTask_ExcludesIt),
            clock: clock);
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        clock.Advance(TimeSpan.FromDays(31));

        // Act
        var result = await sut.GetStaleTasksAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // ── GetStatusSummaryAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetStatusSummaryAsync_CountsEachStatusCorrectly()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetStatusSummaryAsync_CountsEachStatusCorrectly));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Waiting),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetStatusSummaryAsync();

        // Assert
        result.TodoCount.Should().Be(2);
        result.InProgressCount.Should().Be(1);
        result.WaitingCount.Should().Be(1);
        result.DoneCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusSummaryAsync_CountsDoneCorrectly()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetStatusSummaryAsync_CountsDoneCorrectly));
        var project = CreateProject(DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done),
            CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetStatusSummaryAsync();

        // Assert
        result.DoneCount.Should().Be(3);
        result.TodoCount.Should().Be(0);
        result.InProgressCount.Should().Be(0);
        result.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task GetStatusSummaryAsync_ExcludesOtherUsersTasks()
    {
        // Arrange
        var dbName = nameof(GetStatusSummaryAsync_ExcludesOtherUsersTasks);
        var (sut, db) = BuildService(dbName, "u1");
        var myProject = CreateProject("u1");
        var otherProject = CreateProject("other-user");
        db.Projects.AddRange(myProject, otherProject);
        db.Tasks.AddRange(
            CreateTask(myProject.Id, "u1", status: TaskItemStatus.Todo),
            CreateTask(otherProject.Id, "other-user", status: TaskItemStatus.Todo),
            CreateTask(otherProject.Id, "other-user", status: TaskItemStatus.InProgress));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetStatusSummaryAsync();

        // Assert
        result.TodoCount.Should().Be(1);
        result.InProgressCount.Should().Be(0);
    }

    // ── ComputeAttentionScore (pure unit tests, no DB) ────────────────────────

    [Fact]
    public void ComputeAttentionScore_WithOverdueTask_ScoresAbove50()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(ComputeAttentionScore_WithOverdueTask_ScoresAbove50));
        var task = new TasksHubTaskDto(
            Guid.NewGuid(), "Overdue Task", null,
            TaskItemStatus.Todo, TaskPriority.Medium,
            DueDate: Today.AddDays(-3),
            Guid.NewGuid(), "Project",
            DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var score = sut.ComputeAttentionScore(task);

        // Assert
        score.Should().BeGreaterThan(50);
    }

    [Fact]
    public void ComputeAttentionScore_WithDueTodayTask_ScoresAtLeast20()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(ComputeAttentionScore_WithDueTodayTask_ScoresAtLeast20));
        var task = new TasksHubTaskDto(
            Guid.NewGuid(), "Due Today", null,
            TaskItemStatus.Todo, TaskPriority.Low,
            DueDate: Today,
            Guid.NewGuid(), "Project",
            DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var score = sut.ComputeAttentionScore(task);

        // Assert
        score.Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public void ComputeAttentionScore_WithCriticalPriority_Adds20Points()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(ComputeAttentionScore_WithCriticalPriority_Adds20Points));
        // Low priority contributes 0 points, so the delta isolates the Critical bonus.
        var baseTask = new TasksHubTaskDto(
            Guid.NewGuid(), "Base Task", null,
            TaskItemStatus.Todo, TaskPriority.Low,
            DueDate: null,
            Guid.NewGuid(), "Project",
            DateTime.UtcNow, DateTime.UtcNow);

        var criticalTask = baseTask with { Priority = TaskPriority.Critical };

        // Act
        var baseScore = sut.ComputeAttentionScore(baseTask);
        var criticalScore = sut.ComputeAttentionScore(criticalTask);

        // Assert
        (criticalScore - baseScore).Should().Be(20);
    }

    [Fact]
    public void ComputeAttentionScore_ScoreIsCappedAt100()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(ComputeAttentionScore_ScoreIsCappedAt100));
        // Worst possible: critically overdue + critical priority
        var task = new TasksHubTaskDto(
            Guid.NewGuid(), "Extremely Critical", null,
            TaskItemStatus.Todo, TaskPriority.Critical,
            DueDate: Today.AddDays(-365),
            Guid.NewGuid(), "Project",
            DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var score = sut.ComputeAttentionScore(task);

        // Assert
        score.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void ComputeAttentionScore_WithNoDueDateAndLowPriority_ScoresLow()
    {
        // Arrange
        var (sut, _) = BuildService(nameof(ComputeAttentionScore_WithNoDueDateAndLowPriority_ScoresLow));
        var task = new TasksHubTaskDto(
            Guid.NewGuid(), "Low Urgency Task", null,
            TaskItemStatus.Todo, TaskPriority.Low,
            DueDate: null,
            Guid.NewGuid(), "Project",
            DateTime.UtcNow, DateTime.UtcNow);

        // Act
        var score = sut.ComputeAttentionScore(task);

        // Assert
        score.Should().BeLessThan(20);
    }

    // ── SearchTasksAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchTasksAsync_MatchesByTitleCaseInsensitive_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(SearchTasksAsync_MatchesByTitleCaseInsensitive_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.Title = "Implement feature";
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SearchTasksAsync("IMPLEMENT", page: 1, pageSize: 20);

        // Assert
        result.Items.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task SearchTasksAsync_MatchesByDescription_ReturnsIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(SearchTasksAsync_MatchesByDescription_ReturnsIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.Title = "Some task";
        task.Description = "Contains keyword in description";
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SearchTasksAsync("keyword", page: 1, pageSize: 20);

        // Assert
        result.Items.Should().ContainSingle()
            .Which.Id.Should().Be(task.Id);
    }

    [Fact]
    public async Task SearchTasksAsync_WithArchivedTask_ExcludesIt()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(SearchTasksAsync_WithArchivedTask_ExcludesIt));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isArchived: true);
        task.Title = "Archived searchable task";
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SearchTasksAsync("searchable", page: 1, pageSize: 20);

        // Assert
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchTasksAsync_ReturnsPaginationMetadata()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(SearchTasksAsync_ReturnsPaginationMetadata));
        var project = CreateProject(DefaultUserId);
        for (var i = 0; i < 5; i++)
        {
            var task = CreateTask(project.Id, DefaultUserId);
            task.Title = $"Paged task {i}";
            db.Tasks.Add(task);
        }
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SearchTasksAsync("Paged task", page: 1, pageSize: 3);

        // Assert
        result.TotalCount.Should().Be(5);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchTasksAsync_WithNoMatch_ReturnsEmpty()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(SearchTasksAsync_WithNoMatch_ReturnsEmpty));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.Title = "Normal task title";
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.SearchTasksAsync("xyznotexists", page: 1, pageSize: 20);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    // ── GetFilteredTasksAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetFilteredTasksAsync_FilterByProjectId_ReturnsOnlyThatProjectsTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetFilteredTasksAsync_FilterByProjectId_ReturnsOnlyThatProjectsTasks));
        var projectA = CreateProject(DefaultUserId);
        var projectB = CreateProject(DefaultUserId);
        var taskA = CreateTask(projectA.Id, DefaultUserId);
        var taskB = CreateTask(projectB.Id, DefaultUserId);
        db.Projects.AddRange(projectA, projectB);
        db.Tasks.AddRange(taskA, taskB);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(ProjectId: projectA.Id));

        // Assert
        result.Items.Should().ContainSingle()
            .Which.Id.Should().Be(taskA.Id);
    }

    [Fact]
    public async Task GetFilteredTasksAsync_FilterByStatus_ReturnsMatchingTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetFilteredTasksAsync_FilterByStatus_ReturnsMatchingTasks));
        var project = CreateProject(DefaultUserId);
        var inProgress = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var todo = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo);
        db.Projects.Add(project);
        db.Tasks.AddRange(inProgress, todo);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(Status: TaskItemStatus.InProgress));

        // Assert
        result.Items.Should().ContainSingle()
            .Which.Id.Should().Be(inProgress.Id);
    }

    [Fact]
    public async Task GetFilteredTasksAsync_FilterByMinPriority_ReturnsHigherOrEqualPriorityTasks()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetFilteredTasksAsync_FilterByMinPriority_ReturnsHigherOrEqualPriorityTasks));
        var project = CreateProject(DefaultUserId);
        var critical = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Critical);
        var high = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.High);
        var low = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Low);
        db.Projects.Add(project);
        db.Tasks.AddRange(critical, high, low);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(MinPriority: TaskPriority.High));

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Select(t => t.Id).Should().Contain([critical.Id, high.Id]);
    }

    [Fact]
    public async Task GetFilteredTasksAsync_FilterByDueBefore_ReturnsTasksDueBeforeDate()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetFilteredTasksAsync_FilterByDueBefore_ReturnsTasksDueBeforeDate));
        var project = CreateProject(DefaultUserId);
        var dueEarly = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(2));
        var dueLate = CreateTask(project.Id, DefaultUserId, dueDate: Today.AddDays(10));
        db.Projects.Add(project);
        db.Tasks.AddRange(dueEarly, dueLate);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(DueBefore: Today.AddDays(5)));

        // Assert
        result.Items.Should().ContainSingle()
            .Which.Id.Should().Be(dueEarly.Id);
    }

    [Fact]
    public async Task GetFilteredTasksAsync_Page2_ReturnsCorrectItems()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetFilteredTasksAsync_Page2_ReturnsCorrectItems));
        var project = CreateProject(DefaultUserId);
        for (var i = 0; i < 5; i++)
            db.Tasks.Add(CreateTask(project.Id, DefaultUserId));
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // Act
        var page1 = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(Page: 1, PageSize: 3));
        var page2 = await sut.GetFilteredTasksAsync(new TasksHubFilterDto(Page: 2, PageSize: 3));

        // Assert
        page1.Items.Should().HaveCount(3);
        page2.Items.Should().HaveCount(2);
        page2.TotalCount.Should().Be(5);
        page1.Items.Select(t => t.Id).Should().NotIntersectWith(page2.Items.Select(t => t.Id));
    }

    // ── BulkOperationAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task BulkOperationAsync_ChangesStatusForMultipleTasks_ReturnsUpdatedCount()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(BulkOperationAsync_ChangesStatusForMultipleTasks_ReturnsUpdatedCount));
        var project = CreateProject(DefaultUserId);
        var task1 = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo);
        var task2 = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo);
        db.Projects.Add(project);
        db.Tasks.AddRange(task1, task2);
        await db.SaveChangesAsync();

        // Act
        var count = await sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [task1.Id, task2.Id],
            NewStatus: TaskItemStatus.InProgress));

        // Assert
        count.Should().Be(2);
        var updated = await db.Tasks.Where(t => t.Id == task1.Id || t.Id == task2.Id).ToListAsync();
        updated.Should().AllSatisfy(t => t.Status.Should().Be(TaskItemStatus.InProgress));
    }

    [Fact]
    public async Task BulkOperationAsync_ChangesPriorityForMultipleTasks_ReturnsUpdatedCount()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(BulkOperationAsync_ChangesPriorityForMultipleTasks_ReturnsUpdatedCount));
        var project = CreateProject(DefaultUserId);
        var task1 = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Low);
        var task2 = CreateTask(project.Id, DefaultUserId, priority: TaskPriority.Low);
        db.Projects.Add(project);
        db.Tasks.AddRange(task1, task2);
        await db.SaveChangesAsync();

        // Act
        var count = await sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [task1.Id, task2.Id],
            NewPriority: TaskPriority.High));

        // Assert
        count.Should().Be(2);
        var updated = await db.Tasks.Where(t => t.Id == task1.Id || t.Id == task2.Id).ToListAsync();
        updated.Should().AllSatisfy(t => t.Priority.Should().Be(TaskPriority.High));
    }

    [Fact]
    public async Task BulkOperationAsync_ArchivesMultipleTasks_ReturnsUpdatedCount()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(BulkOperationAsync_ArchivesMultipleTasks_ReturnsUpdatedCount));
        var project = CreateProject(DefaultUserId);
        var task1 = CreateTask(project.Id, DefaultUserId);
        var task2 = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(task1, task2);
        await db.SaveChangesAsync();

        // Act
        var count = await sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [task1.Id, task2.Id],
            Archive: true));

        // Assert
        count.Should().Be(2);
        var updated = await db.Tasks.Where(t => t.Id == task1.Id || t.Id == task2.Id).ToListAsync();
        updated.Should().AllSatisfy(t => t.IsArchived.Should().BeTrue());
    }

    [Fact]
    public async Task BulkOperationAsync_CompletingRecurringTask_CreatesNextOccurrence()
    {
        var (sut, db) = BuildService(nameof(BulkOperationAsync_CompletingRecurringTask_CreatesNextOccurrence));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.IsRecurring = true;
        task.RecurrenceType = RecurrenceType.Daily;
        task.RecurrenceInterval = 1;
        task.NextOccurrenceDate = Today.AddDays(1);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        await sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [task.Id], NewStatus: TaskItemStatus.Done));

        (await db.Tasks.AsNoTracking().CountAsync(t => t.RecurrenceSourceTaskId == task.Id)).Should().Be(1);
    }

    [Fact]
    public async Task BulkOperationAsync_WithIncompletePrerequisite_RejectsCompletion()
    {
        var (sut, db) = BuildService(nameof(BulkOperationAsync_WithIncompletePrerequisite_RejectsCompletion));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = dependent.Id,
            DependsOnTaskId = prerequisite.Id,
        });
        await db.SaveChangesAsync();

        var act = () => sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [dependent.Id],
            NewStatus: TaskItemStatus.Done));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*prerequisite*");
        (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == dependent.Id))
            .Status.Should().Be(TaskItemStatus.Todo);
    }

    [Fact]
    public async Task BulkOperationAsync_ArchivingParent_CascadesWithSharedOperationId()
    {
        var (sut, db) = BuildService(nameof(BulkOperationAsync_ArchivingParent_CascadesWithSharedOperationId));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        await sut.BulkOperationAsync(new BulkTaskOperationDto(TaskIds: [parent.Id], Archive: true));

        var archived = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id);
        archived[subtask.Id].IsArchived.Should().BeTrue();
        archived[parent.Id].ArchiveOperationId.Should().Be(archived[subtask.Id].ArchiveOperationId);
    }

    [Fact]
    public async Task BulkOperationAsync_IgnoresTasksNotOwnedByCurrentUser_DoesNotUpdateThem()
    {
        // Arrange
        var dbName = nameof(BulkOperationAsync_IgnoresTasksNotOwnedByCurrentUser_DoesNotUpdateThem);
        var (sut, db) = BuildService(dbName, "u1");
        var myProject = CreateProject("u1");
        var otherProject = CreateProject("other-user");
        var myTask = CreateTask(myProject.Id, "u1", status: TaskItemStatus.Todo);
        var otherTask = CreateTask(otherProject.Id, "other-user", status: TaskItemStatus.Todo);
        db.Projects.AddRange(myProject, otherProject);
        db.Tasks.AddRange(myTask, otherTask);
        await db.SaveChangesAsync();

        // Act
        var count = await sut.BulkOperationAsync(new BulkTaskOperationDto(
            TaskIds: [myTask.Id, otherTask.Id],
            NewStatus: TaskItemStatus.Done));

        // Assert
        count.Should().Be(1);
        var otherTaskInDb = await db.Tasks.FindAsync(otherTask.Id);
        otherTaskInDb!.Status.Should().Be(TaskItemStatus.Todo);
    }

    // ── GetHubAggregateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetHubAggregateAsync_ExcludesArchivedTasksInAllSections()
    {
        // Arrange
        var (sut, db) = BuildService(nameof(GetHubAggregateAsync_ExcludesArchivedTasksInAllSections));
        var project = CreateProject(DefaultUserId);
        // Archived tasks that would otherwise qualify for every section
        db.Projects.Add(project);
        db.Tasks.AddRange(
            // Active section — overdue, high priority, on hold, stale, no due date, attention
            CreateTask(project.Id, DefaultUserId,
                dueDate: Today.AddDays(-1), priority: TaskPriority.Critical,
                status: TaskItemStatus.Waiting, isArchived: true,
                updatedAt: DateTime.UtcNow.AddDays(-31)));
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetHubAggregateAsync();

        // Assert
        result.ActiveTasks.Should().BeEmpty();
        result.HighPriorityTasks.Should().BeEmpty();
        result.OnHoldTasks.Should().BeEmpty();
        result.OverdueTasks.Should().BeEmpty();
        result.NeedingAttentionTasks.Should().BeEmpty();
        result.StaleTasks.Should().BeEmpty();
    }
}
