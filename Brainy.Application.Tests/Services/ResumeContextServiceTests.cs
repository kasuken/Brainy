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
/// Unit tests for <see cref="IResumeContextService"/> (issue #305), resolved via the real
/// DI container with an EF Core InMemory database. Covers task state (archived/waiting),
/// dependency resolution, and strict per-user scoping of related records.
/// </summary>
public class ResumeContextServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IResumeContextService sut, BrainyDbContext db, FixedTimeProvider clock) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        // The DbContext stamps CreatedAtUtc/UpdatedAtUtc from TimeProvider on every SaveChanges,
        // overwriting whatever a test sets directly — a fixed, advanceable clock lets tests that
        // depend on "most recently updated" ordering control that ordering deterministically.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(clock);

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IResumeContextService>(), sp.GetRequiredService<BrainyDbContext>(), clock);
    }

    private static Project CreateProject(string userId, Guid? goalId = null, DateTime? dueDate = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Project",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.Medium,
            DueDate = dueDate,
            GoalId = goalId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static Goal CreateGoal(string userId, DateTime? targetDate = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Test Goal",
            TargetDate = targetDate,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static TaskItem CreateTask(
        Guid projectId,
        string userId,
        TaskItemStatus status = TaskItemStatus.InProgress,
        bool isArchived = false,
        string title = "Test Task")
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            Title = title,
            Status = status,
            Priority = TaskPriority.Medium,
            IsArchived = isArchived,
            IsCurrentTask = status == TaskItemStatus.InProgress,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    // ── GetAsync: task state ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTaskNotOwnedByCurrentUser()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ReturnsNull_WhenTaskNotOwnedByCurrentUser));
        var project = CreateProject(OtherUserId);
        var task = CreateTask(project.Id, OtherUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReportsArchivedAndWaitingStateAccurately()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ReportsArchivedAndWaitingStateAccurately));
        var project = CreateProject(DefaultUserId);
        var archivedTask = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Waiting, isArchived: true);
        db.Projects.Add(project);
        db.Tasks.Add(archivedTask);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(archivedTask.Id);

        result.Should().NotBeNull();
        result!.IsArchived.Should().BeTrue();
        result.TaskStatus.Should().Be(TaskItemStatus.Waiting);
    }

    // ── GetAsync: next actionable step ──────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsFirstNotDoneSubtaskInOrder_AsNextStep()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ReturnsFirstNotDoneSubtaskInOrder_AsNextStep));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);

        var doneSubtask = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done, title: "Done first");
        doneSubtask.ParentTaskId = task.Id;
        doneSubtask.SortOrder = 0;

        var nextSubtask = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, title: "Next up");
        nextSubtask.ParentTaskId = task.Id;
        nextSubtask.SortOrder = 1;

        db.Tasks.AddRange(doneSubtask, nextSubtask);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.HasSubtasks.Should().BeTrue();
        result.NextSubtaskTitle.Should().Be("Next up");
    }

    [Fact]
    public async Task GetAsync_ReportsNoSubtasks_WhenTaskHasNone()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ReportsNoSubtasks_WhenTaskHasNone));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.HasSubtasks.Should().BeFalse();
        result.NextSubtaskTitle.Should().BeNull();
    }

    // ── GetAsync: dependency resolution ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReportsBlocked_WhenPrerequisiteIsIncomplete()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ReportsBlocked_WhenPrerequisiteIsIncomplete));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Todo, title: "Prerequisite");
        db.Projects.Add(project);
        db.Tasks.AddRange(task, prerequisite);
        db.TaskDependencies.Add(new TaskDependency { Id = Guid.NewGuid(), TaskId = task.Id, DependsOnTaskId = prerequisite.Id });
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.IsBlocked.Should().BeTrue();
        result.UnresolvedDependencyTitle.Should().Be("Prerequisite");
    }

    [Fact]
    public async Task GetAsync_IsNotBlocked_WhenPrerequisiteIsDone()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_IsNotBlocked_WhenPrerequisiteIsDone));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        var prerequisite = CreateTask(project.Id, DefaultUserId, status: TaskItemStatus.Done, title: "Prerequisite");
        db.Projects.Add(project);
        db.Tasks.AddRange(task, prerequisite);
        db.TaskDependencies.Add(new TaskDependency { Id = Guid.NewGuid(), TaskId = task.Id, DependsOnTaskId = prerequisite.Id });
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.IsBlocked.Should().BeFalse();
        result.UnresolvedDependencyTitle.Should().BeNull();
    }

    // ── GetAsync: most recently linked item, user-scoped ────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsMostRecentlyUpdatedNoteInProject()
    {
        var (sut, db, clock) = BuildService(nameof(GetAsync_ReturnsMostRecentlyUpdatedNoteInProject));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // BrainyDbContext stamps UpdatedAtUtc from the current TimeProvider on every
        // SaveChanges, overwriting values set directly on the entity — advance the fixed
        // clock between saves so "most recently updated" has a real, deterministic ordering.
        var older = new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, ProjectId = project.Id, Title = "Older note" };
        db.Notes.Add(older);
        await db.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(1));

        var newer = new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, ProjectId = project.Id, Title = "Newer note" };
        db.Notes.Add(newer);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.MostRecentLinkedItem.Should().NotBeNull();
        result.MostRecentLinkedItem!.Title.Should().Be("Newer note");
    }

    [Fact]
    public async Task GetAsync_NeverReturnsAnotherUsersNoteAsLinkedItem()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_NeverReturnsAnotherUsersNoteAsLinkedItem));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);

        // Same project id, but owned by a different user — must never surface here.
        var otherUsersNote = new Note
        {
            Id = Guid.NewGuid(), UserId = OtherUserId, ProjectId = project.Id, Title = "Someone else's note",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        db.Notes.Add(otherUsersNote);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.MostRecentLinkedItem.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExcludesArchivedNotesFromLinkedItem()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_ExcludesArchivedNotesFromLinkedItem));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);

        var archivedNote = new Note
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, ProjectId = project.Id, Title = "Archived note",
            IsArchived = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        db.Notes.Add(archivedNote);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.MostRecentLinkedItem.Should().BeNull();
    }

    // ── Project / goal context ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_IncludesProjectAndGoalDueDates()
    {
        var (sut, db, _) = BuildService(nameof(GetAsync_IncludesProjectAndGoalDueDates));
        var goalDueDate = DateTime.UtcNow.Date.AddDays(30);
        var projectDueDate = DateTime.UtcNow.Date.AddDays(10);
        var goal = CreateGoal(DefaultUserId, goalDueDate);
        var project = CreateProject(DefaultUserId, goal.Id, projectDueDate);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Goals.Add(goal);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.GetAsync(task.Id);

        result.Should().NotBeNull();
        result!.ProjectDueDate.Should().Be(projectDueDate);
        result.GoalTitle.Should().Be("Test Goal");
        result.GoalDueDate.Should().Be(goalDueDate);
    }

    // ── SaveRestartNoteAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SaveRestartNoteAsync_PersistsTrimmedNote()
    {
        var (sut, db, _) = BuildService(nameof(SaveRestartNoteAsync_PersistsTrimmedNote));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.SaveRestartNoteAsync(task.Id, "  Pick up from the API contract draft  ");

        result.RestartNote.Should().Be("Pick up from the API contract draft");
    }

    [Fact]
    public async Task SaveRestartNoteAsync_ClearsNote_WhenWhitespaceOnly()
    {
        var (sut, db, _) = BuildService(nameof(SaveRestartNoteAsync_ClearsNote_WhenWhitespaceOnly));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project.Id, DefaultUserId);
        task.RestartNote = "Existing note";
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var result = await sut.SaveRestartNoteAsync(task.Id, "   ");

        result.RestartNote.Should().BeNull();
    }

    [Fact]
    public async Task SaveRestartNoteAsync_Throws_WhenTaskNotOwnedByCurrentUser()
    {
        var (sut, db, _) = BuildService(nameof(SaveRestartNoteAsync_Throws_WhenTaskNotOwnedByCurrentUser));
        var project = CreateProject(OtherUserId);
        var task = CreateTask(project.Id, OtherUserId);
        db.Projects.Add(project);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var act = async () => await sut.SaveRestartNoteAsync(task.Id, "Should not be allowed");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
