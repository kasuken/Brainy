using Brainy.Application.Common;
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
/// Integration tests for <see cref="ITaskService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Focuses on subtask behaviour: nested projection and automatic parent progress.
/// </summary>
public class TaskServiceTests
{
    private const string DefaultUserId = "u1";

    private static (ITaskService sut, BrainyDbContext db) BuildService(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ITaskService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(string userId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Project",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.Medium,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static TaskItem CreateTask(
        Guid projectId,
        string userId,
        TaskItemStatus status = TaskItemStatus.Todo,
        Guid? parentTaskId = null,
        bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Task",
            Status = status,
            Priority = TaskPriority.Medium,
            IsArchived = isArchived,
            ParentTaskId = parentTaskId,
            CompletedDate = status == TaskItemStatus.Done ? DateTime.UtcNow : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    // ── GetByProjectAsync projection ──────────────────────────────────────────

    [Fact]
    public async Task GetByProjectAsync_ReturnsOnlyTopLevelTasks()
    {
        var (sut, db) = BuildService(nameof(GetByProjectAsync_ReturnsOnlyTopLevelTasks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        var result = await sut.GetByProjectAsync(project.Id);

        result.Should().ContainSingle().Which.Id.Should().Be(parent.Id);
    }

    [Fact]
    public async Task GetByProjectAsync_PopulatesSubtasksAndCounts()
    {
        var (sut, db) = BuildService(nameof(GetByProjectAsync_PopulatesSubtasksAndCounts));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var doneSub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done, parentTaskId: parent.Id);
        var openSub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, doneSub, openSub);
        await db.SaveChangesAsync();

        var result = await sut.GetByProjectAsync(project.Id);
        var dto = result.Single();

        dto.SubtaskCount.Should().Be(2);
        dto.DoneSubtaskCount.Should().Be(1);
        dto.Subtasks.Should().NotBeNull();
        dto.Subtasks!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByProjectAsync_ExcludesArchivedSubtasksFromCounts()
    {
        var (sut, db) = BuildService(nameof(GetByProjectAsync_ExcludesArchivedSubtasksFromCounts));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var activeSub = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        var archivedSub = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, activeSub, archivedSub);
        await db.SaveChangesAsync();

        var dto = (await sut.GetByProjectAsync(project.Id)).Single();

        dto.SubtaskCount.Should().Be(1);
    }

    [Fact]
    public async Task GetByProjectAsync_OrdersTopLevelTasksByPrioritySeverity()
    {
        var (sut, db) = BuildService(nameof(GetByProjectAsync_OrdersTopLevelTasksByPrioritySeverity));
        var project = CreateProject(DefaultUserId);
        var low = CreateTask(project.Id, DefaultUserId);
        low.Priority = TaskPriority.Low;
        var critical = CreateTask(project.Id, DefaultUserId);
        critical.Priority = TaskPriority.Critical;
        var medium = CreateTask(project.Id, DefaultUserId);
        medium.Priority = TaskPriority.Medium;
        db.Projects.Add(project);
        db.Tasks.AddRange(low, critical, medium);
        await db.SaveChangesAsync();

        var result = await sut.GetByProjectAsync(project.Id);

        result.Select(t => t.Priority).Should()
            .ContainInOrder(TaskPriority.Critical, TaskPriority.Medium, TaskPriority.Low);
    }

    [Fact]
    public async Task GetByProjectAsync_OrdersSubtasksByPrioritySeverity()
    {
        var (sut, db) = BuildService(nameof(GetByProjectAsync_OrdersSubtasksByPrioritySeverity));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var lowSub = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        lowSub.Priority = TaskPriority.Low;
        var criticalSub = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        criticalSub.Priority = TaskPriority.Critical;
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, lowSub, criticalSub);
        await db.SaveChangesAsync();

        var dto = (await sut.GetByProjectAsync(project.Id)).Single();

        dto.Subtasks!.Select(s => s.Priority).Should()
            .ContainInOrder(TaskPriority.Critical, TaskPriority.Low);
    }

    // ── Automatic parent progress ─────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_WhenLastSubtaskCompleted_AutoCompletesParent()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenLastSubtaskCompleted_AutoCompletesParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var sub = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, sub);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(sub.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == parent.Id);
        refreshed.Status.Should().Be(TaskItemStatus.Done);
    }

    [Fact]
    public async Task CompleteAsync_WhenOneSubtaskStillOpen_LeavesParentUnchanged()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenOneSubtaskStillOpen_LeavesParentUnchanged));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var sub1 = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        var sub2 = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, sub1, sub2);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(sub1.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == parent.Id);
        refreshed.Status.Should().Be(TaskItemStatus.InProgress);
    }

    [Fact]
    public async Task ReopenAsync_WhenSubtaskReopenedOnDoneParent_ReopensParentToInProgress()
    {
        var (sut, db) = BuildService(nameof(ReopenAsync_WhenSubtaskReopenedOnDoneParent_ReopensParentToInProgress));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done);
        var sub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, sub);
        await db.SaveChangesAsync();

        await sut.ReopenAsync(sub.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == parent.Id);
        refreshed.Status.Should().Be(TaskItemStatus.InProgress);
    }

    [Fact]
    public async Task CreateAsync_WhenSubtaskAddedToDoneParent_ReopensParent()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenSubtaskAddedToDoneParent_ReopensParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done);
        db.Projects.Add(project);
        db.Tasks.Add(parent);
        await db.SaveChangesAsync();

        await sut.CreateAsync(new CreateTaskDto(project.Id, "New subtask", ParentTaskId: parent.Id));

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == parent.Id);
        refreshed.Status.Should().Be(TaskItemStatus.InProgress);
    }

    [Fact]
    public async Task ArchiveAsync_WhenRemainingSubtasksAllDone_AutoCompletesParent()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_WhenRemainingSubtasksAllDone_AutoCompletesParent));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var doneSub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done, parentTaskId: parent.Id);
        var openSub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, doneSub, openSub);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(openSub.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == parent.Id);
        refreshed.Status.Should().Be(TaskItemStatus.Done);
    }

    [Fact]
    public async Task ArchiveAsync_WithReason_PersistsReasonForTaskAndSubtasks()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_WithReason_PersistsReasonForTaskAndSubtasks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(parent.Id, archivedReason: "Waiting for next quarter");

        var tasks = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id);
        tasks[parent.Id].ArchivedReason.Should().Be("Waiting for next quarter");
        tasks[subtask.Id].ArchivedReason.Should().Be("Waiting for next quarter");
    }

    // ── Cascade completion down to subtasks ───────────────────────────────────

    [Fact]
    public async Task CompleteAsync_WhenParentCompleted_CompletesAllActiveSubtasks()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenParentCompleted_CompletesAllActiveSubtasks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var sub1 = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, parentTaskId: parent.Id);
        var sub2 = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, sub1, sub2);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(parent.Id);

        var subs = await db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId == parent.Id)
            .ToListAsync();
        subs.Should().OnlyContain(s => s.Status == TaskItemStatus.Done);
    }

    [Fact]
    public async Task CompleteAsync_WhenParentCompleted_DoesNotCompleteArchivedSubtasks()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenParentCompleted_DoesNotCompleteArchivedSubtasks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var archivedSub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, parentTaskId: parent.Id, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, archivedSub);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(parent.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == archivedSub.Id);
        refreshed.Status.Should().Be(TaskItemStatus.Todo);
    }

    [Fact]
    public async Task CompleteAsync_WhenParentCompleted_SetsSubtaskCompletedDate()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenParentCompleted_SetsSubtaskCompletedDate));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.InProgress);
        var sub = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, sub);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(parent.Id);

        var refreshed = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == sub.Id);
        refreshed.CompletedDate.Should().NotBeNull();
    }

    // ── Optimistic concurrency ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // The InMemory provider validates concurrency tokens but does not regenerate
        // them on update, so a mismatched token simulates the stale value a second
        // tab would hold after SQL Server bumps the rowversion.
        var act = () => sut.UpdateAsync(new UpdateTaskDto(
            task.Id, "My stale edit", RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── Delete with dependency links ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenTaskIsDependedOnByAnotherTask_RemovesDependencyLinks()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenTaskIsDependedOnByAnotherTask_RemovesDependencyLinks));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = dependent.Id,
            DependsOnTaskId = prerequisite.Id
        });
        await db.SaveChangesAsync();

        await sut.DeleteAsync(prerequisite.Id);

        (await db.TaskDependencies.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenSubtaskHasDependencyLink_RemovesDependencyLinks()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenSubtaskHasDependencyLink_RemovesDependencyLinks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        var other = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask, other);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = subtask.Id,
            DependsOnTaskId = other.Id
        });
        await db.SaveChangesAsync();

        await sut.DeleteAsync(parent.Id);

        (await db.TaskDependencies.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetCurrentTaskAsync_WhenTargetIsArchived_PreservesExistingCurrentTask()
    {
        var (sut, db) = BuildService(nameof(SetCurrentTaskAsync_WhenTargetIsArchived_PreservesExistingCurrentTask));
        var project = CreateProject(DefaultUserId);
        var current = CreateTask(project.Id, DefaultUserId, TaskItemStatus.InProgress);
        current.IsCurrentTask = true;
        var archived = CreateTask(project.Id, DefaultUserId, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(current, archived);
        await db.SaveChangesAsync();

        var act = () => sut.SetCurrentTaskAsync(archived.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == current.Id)).IsCurrentTask.Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveAndRestoreAsync_RestoresOnlyTasksFromSameArchiveOperation()
    {
        var (sut, db) = BuildService(nameof(ArchiveAndRestoreAsync_RestoresOnlyTasksFromSameArchiveOperation));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var activeSubtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        var previouslyArchived = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id, isArchived: true);
        previouslyArchived.ArchivedAtUtc = DateTime.UtcNow.AddDays(-2);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, activeSubtask, previouslyArchived);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(parent.Id);
        await sut.RestoreAsync(parent.Id);

        var tasks = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id);
        tasks[parent.Id].IsArchived.Should().BeFalse();
        tasks[activeSubtask.Id].IsArchived.Should().BeFalse();
        tasks[previouslyArchived.Id].IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_ClearsArchivedReasonFromRestoredTasks()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_ClearsArchivedReasonFromRestoredTasks));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(parent.Id, archivedReason: "Reference only");
        await sut.RestoreAsync(parent.Id);

        var tasks = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id);
        tasks[parent.Id].ArchivedReason.Should().BeNull();
        tasks[subtask.Id].ArchivedReason.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_WhenParentTaskIsArchived_RejectsSubtaskRestore()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_WhenParentTaskIsArchived_RejectsSubtaskRestore));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, isArchived: true);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        var act = () => sut.RestoreAsync(subtask.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*parent task*");
        (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == subtask.Id)).IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllArchivedAsync_ReturnsProjectContextAndRestoreCapability()
    {
        var (sut, db) = BuildService(nameof(GetAllArchivedAsync_ReturnsProjectContextAndRestoreCapability));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId, isArchived: true);
        task.ArchivedAtUtc = DateTime.UtcNow;
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var archived = await sut.GetAllArchivedAsync();

        archived.Should().ContainSingle();
        archived[0].ProjectName.Should().Be(project.Name);
        archived[0].CanRestore.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllArchivedAsync_WhenParentTaskIsArchived_MarksSubtaskNotRestorable()
    {
        var (sut, db) = BuildService(nameof(GetAllArchivedAsync_WhenParentTaskIsArchived_MarksSubtaskNotRestorable));
        var project = CreateProject(DefaultUserId);
        var parent = CreateTask(project.Id, DefaultUserId, isArchived: true);
        var subtask = CreateTask(project.Id, DefaultUserId, parentTaskId: parent.Id, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.AddRange(parent, subtask);
        await db.SaveChangesAsync();

        var archived = await sut.GetAllArchivedAsync();

        archived.Single(task => task.Id == subtask.Id).CanRestore.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_RecurringTask_CreatesOneIdempotentNextOccurrence()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_RecurringTask_CreatesOneIdempotentNextOccurrence));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.IsRecurring = true;
        task.RecurrenceType = RecurrenceType.Weekly;
        task.RecurrenceInterval = 1;
        task.NextOccurrenceDate = new DateTime(2026, 8, 20);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(task.Id);
        await sut.CompleteAsync(task.Id);

        var occurrences = await db.Tasks.AsNoTracking()
            .Where(t => t.RecurrenceSourceTaskId == task.Id)
            .ToListAsync();
        occurrences.Should().ContainSingle();
        occurrences[0].DueDate.Should().Be(new DateTime(2026, 8, 20));
        occurrences[0].NextOccurrenceDate.Should().Be(new DateTime(2026, 8, 27));
        occurrences[0].IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_WhenNextOccurrenceExceedsEndDate_DoesNotCreateOccurrence()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenNextOccurrenceExceedsEndDate_DoesNotCreateOccurrence));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.IsRecurring = true;
        task.RecurrenceType = RecurrenceType.Daily;
        task.RecurrenceInterval = 1;
        task.NextOccurrenceDate = new DateTime(2026, 8, 21);
        task.RecurrenceEndDate = new DateTime(2026, 8, 20);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(task.Id);

        (await db.Tasks.AsNoTracking().CountAsync(t => t.RecurrenceSourceTaskId == task.Id)).Should().Be(0);
        (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Status.Should().Be(TaskItemStatus.Done);
    }

    [Fact]
    public void GetUpcomingOccurrences_ReturnsNextFiveDatesWithinEndDate()
    {
        var (sut, _) = BuildService(nameof(GetUpcomingOccurrences_ReturnsNextFiveDatesWithinEndDate));

        var occurrences = sut.GetUpcomingOccurrences(
            RecurrenceType.Weekly,
            2,
            new DateTime(2026, 8, 20, 14, 30, 0),
            new DateTime(2026, 10, 20),
            count: 5);

        occurrences.Should().BeEquivalentTo(
            [
                new DateTime(2026, 8, 20),
                new DateTime(2026, 9, 3),
                new DateTime(2026, 9, 17),
                new DateTime(2026, 10, 1),
                new DateTime(2026, 10, 15)
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void GetUpcomingOccurrences_WithIncompleteRule_ReturnsEmptyList()
    {
        var (sut, _) = BuildService(nameof(GetUpcomingOccurrences_WithIncompleteRule_ReturnsEmptyList));

        var occurrences = sut.GetUpcomingOccurrences(
            RecurrenceType.Monthly,
            0,
            new DateTime(2026, 8, 20),
            null);

        occurrences.Should().BeEmpty();
    }

    [Fact]
    public void GetRecurrenceSummary_FormatsReadableSchedule()
    {
        var (sut, _) = BuildService(nameof(GetRecurrenceSummary_FormatsReadableSchedule));

        var summary = sut.GetRecurrenceSummary(
            RecurrenceType.Weekly,
            2,
            new DateTime(2026, 8, 20),
            new DateTime(2027, 1, 1));

        summary.Should().Be("Every 2 weeks, next on Aug 20, 2026, ending Jan 1, 2027");
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_UpdatesAllOwnedTasks()
    {
        var (sut, db) = BuildService(nameof(BulkUpdateStatusAsync_UpdatesAllOwnedTasks));
        var project = CreateProject(DefaultUserId);
        var first = CreateTask(project.Id, DefaultUserId);
        var second = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Waiting);
        db.Projects.Add(project);
        db.Tasks.AddRange(first, second);
        await db.SaveChangesAsync();

        var updated = await sut.BulkUpdateStatusAsync([first.Id, second.Id], TaskItemStatus.Waiting);

        updated.Should().Be(2);
        var storedStatuses = await db.Tasks.AsNoTracking()
            .Where(task => task.Id == first.Id || task.Id == second.Id)
            .Select(task => task.Status)
            .ToListAsync();
        storedStatuses.Should().OnlyContain(status => status == TaskItemStatus.Waiting);
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_WithTaskOwnedByAnotherUser_ThrowsAndLeavesOwnedTasksUnchanged()
    {
        var (sut, db) = BuildService(nameof(BulkUpdateStatusAsync_WithTaskOwnedByAnotherUser_ThrowsAndLeavesOwnedTasksUnchanged));
        var ownProject = CreateProject(DefaultUserId);
        var ownTask = CreateTask(ownProject.Id, DefaultUserId);
        var otherProject = CreateProject("u2");
        var otherUsersTask = CreateTask(otherProject.Id, "u2");
        db.Projects.AddRange(ownProject, otherProject);
        db.Tasks.AddRange(ownTask, otherUsersTask);
        await db.SaveChangesAsync();

        var act = () => sut.BulkUpdateStatusAsync([ownTask.Id, otherUsersTask.Id], TaskItemStatus.Waiting);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*tasks were not found*");
        (await db.Tasks.AsNoTracking().SingleAsync(task => task.Id == ownTask.Id)).Status.Should().Be(TaskItemStatus.Todo);
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_WithEmptyList_ReturnsZero()
    {
        var (sut, db) = BuildService(nameof(BulkUpdateStatusAsync_WithEmptyList_ReturnsZero));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var updated = await sut.BulkUpdateStatusAsync([], TaskItemStatus.Waiting);

        updated.Should().Be(0);
        (await db.Tasks.AsNoTracking().SingleAsync(taskItem => taskItem.Id == task.Id)).Status.Should().Be(TaskItemStatus.Todo);
    }

    [Fact]
    public async Task AddDependencyAsync_RejectsSelfDependency()
    {
        var (sut, db) = BuildService(nameof(AddDependencyAsync_RejectsSelfDependency));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var act = () => sut.AddDependencyAsync(task.Id, task.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddDependencyAsync_RejectsTransitiveCycle()
    {
        var (sut, db) = BuildService(nameof(AddDependencyAsync_RejectsTransitiveCycle));
        var project = CreateProject(DefaultUserId);
        var first = CreateTask(project.Id, DefaultUserId);
        var second = CreateTask(project.Id, DefaultUserId);
        var third = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(first, second, third);
        await db.SaveChangesAsync();
        await sut.AddDependencyAsync(first.Id, second.Id);
        await sut.AddDependencyAsync(second.Id, third.Id);

        var act = () => sut.AddDependencyAsync(third.Id, first.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task UpdateAsync_ReplacingDependenciesRejectsCycle()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_ReplacingDependenciesRejectsCycle));
        var project = CreateProject(DefaultUserId);
        var first = CreateTask(project.Id, DefaultUserId);
        var second = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(first, second);
        await db.SaveChangesAsync();
        await sut.AddDependencyAsync(first.Id, second.Id);

        var act = () => sut.UpdateAsync(new UpdateTaskDto(
            second.Id,
            second.Title,
            DependsOnTaskIds: [first.Id]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done)]
    public async Task AddDependencyAsync_WithIncompletePrerequisite_RejectsActiveTask(TaskItemStatus status)
    {
        var (sut, db) = BuildService(
            $"{nameof(AddDependencyAsync_WithIncompletePrerequisite_RejectsActiveTask)}-{status}");
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId, status: status);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        await db.SaveChangesAsync();

        var act = () => sut.AddDependencyAsync(dependent.Id, prerequisite.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*prerequisite*");
        (await db.TaskDependencies.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPrerequisiteIds()
    {
        var (sut, db) = BuildService(nameof(GetByIdAsync_ReturnsPrerequisiteIds));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = dependent.Id,
            DependsOnTaskId = prerequisite.Id
        });
        await db.SaveChangesAsync();

        var result = await sut.GetByIdAsync(dependent.Id);

        result!.DependsOnTaskIds.Should().ContainSingle().Which.Should().Be(prerequisite.Id);
    }

    [Fact]
    public async Task CreateAsync_WithPrerequisites_PersistsLinks()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithPrerequisites_PersistsLinks));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(prerequisite);
        await db.SaveChangesAsync();

        var created = await sut.CreateAsync(new CreateTaskDto(
            project.Id,
            "Dependent task",
            DependsOnTaskIds: [prerequisite.Id]));

        created.DependsOnTaskIds.Should().ContainSingle().Which.Should().Be(prerequisite.Id);
        (await db.TaskDependencies.AsNoTracking().SingleAsync()).DependsOnTaskId.Should().Be(prerequisite.Id);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesPrerequisites()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_ReplacesPrerequisites));
        var project = CreateProject(DefaultUserId);
        var first = CreateTask(project.Id, DefaultUserId);
        var second = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(first, second, dependent);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = dependent.Id,
            DependsOnTaskId = first.Id
        });
        await db.SaveChangesAsync();

        var result = await sut.UpdateAsync(new UpdateTaskDto(
            dependent.Id,
            dependent.Title,
            DependsOnTaskIds: [second.Id]));

        result.DependsOnTaskIds.Should().ContainSingle().Which.Should().Be(second.Id);
        (await db.TaskDependencies.AsNoTracking().SingleAsync()).DependsOnTaskId.Should().Be(second.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithoutPrerequisitePayload_PreservesLinks()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_WithoutPrerequisitePayload_PreservesLinks));
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        db.TaskDependencies.Add(new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = dependent.Id,
            DependsOnTaskId = prerequisite.Id
        });
        await db.SaveChangesAsync();

        await sut.UpdateAsync(new UpdateTaskDto(dependent.Id, "Renamed"));

        (await db.TaskDependencies.AsNoTracking().SingleAsync()).DependsOnTaskId.Should().Be(prerequisite.Id);
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done)]
    public async Task UpdateAsync_WithIncompletePrerequisite_RejectsStartingOrCompleting(TaskItemStatus status)
    {
        var (sut, db) = BuildService($"{nameof(UpdateAsync_WithIncompletePrerequisite_RejectsStartingOrCompleting)}-{status}");
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        await db.SaveChangesAsync();

        var act = () => sut.UpdateAsync(new UpdateTaskDto(
            dependent.Id,
            dependent.Title,
            Status: status,
            DependsOnTaskIds: [prerequisite.Id]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*prerequisite*");
        (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == dependent.Id)).Status.Should().Be(TaskItemStatus.Todo);
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done)]
    public async Task UpdateAsync_AddingIncompletePrerequisiteToActiveTask_RejectsUpdate(TaskItemStatus status)
    {
        var (sut, db) = BuildService(
            $"{nameof(UpdateAsync_AddingIncompletePrerequisiteToActiveTask_RejectsUpdate)}-{status}");
        var project = CreateProject(DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId);
        var dependent = CreateTask(project.Id, DefaultUserId, status: status);
        db.Projects.Add(project);
        db.Tasks.AddRange(prerequisite, dependent);
        await db.SaveChangesAsync();

        var act = () => sut.UpdateAsync(new UpdateTaskDto(
            dependent.Id,
            "Rejected rename",
            Status: status,
            DependsOnTaskIds: [prerequisite.Id]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*prerequisite*");

        var stored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == dependent.Id);
        stored.Title.Should().Be(dependent.Title);
        (await db.TaskDependencies.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddDependencyAsync_RejectsTaskFromAnotherProject()
    {
        var (sut, db) = BuildService(nameof(AddDependencyAsync_RejectsTaskFromAnotherProject));
        var firstProject = CreateProject(DefaultUserId);
        var secondProject = CreateProject(DefaultUserId);
        var dependent = CreateTask(firstProject.Id, DefaultUserId);
        var prerequisite = CreateTask(secondProject.Id, DefaultUserId);
        db.Projects.AddRange(firstProject, secondProject);
        db.Tasks.AddRange(dependent, prerequisite);
        await db.SaveChangesAsync();

        var act = () => sut.AddDependencyAsync(dependent.Id, prerequisite.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same project*");
    }

}
