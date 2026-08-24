using Brainy.Application.Common;
using Brainy.Application.DTOs.Week;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

public sealed class WeekServiceTests
{
    private const string DefaultUserId = "week-user";
    private static readonly DateTime DefaultToday = new(2026, 6, 17);
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    [Fact]
    public async Task GetCurrentWeekOverviewAsync_UsesMondaySundayWindow()
    {
        var fixture = BuildFixture(nameof(GetCurrentWeekOverviewAsync_UsesMondaySundayWindow));

        var overview = await fixture.Week.GetCurrentWeekOverviewAsync();

        overview.WeekStartDate.Should().Be(new DateTime(2026, 6, 15));
        overview.WeekEndDate.Should().Be(new DateTime(2026, 6, 21));
        overview.WeekNumber.Should().Be(25);
    }

    [Fact]
    public async Task GetCurrentWeekOverviewAsync_UsesUserCalendarDateNearUtcBoundary()
    {
        var timeZoneId = FindPreferredTimeZoneId();
        if (timeZoneId is null)
            return;

        var fixedNow = new DateTimeOffset(2026, 6, 15, 0, 30, 0, TimeSpan.Zero);
        var fixture = BuildFixture(
            nameof(GetCurrentWeekOverviewAsync_UsesUserCalendarDateNearUtcBoundary),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedNow));
            },
            useFakeTimeZoneService: false);

        fixture.Db.DashboardPreferences.Add(new UserDashboardPreference
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            TimeZoneId = timeZoneId
        });
        await fixture.Db.SaveChangesAsync();

        var overview = await fixture.Week.GetCurrentWeekOverviewAsync();

        overview.Today.Should().Be(new DateTime(2026, 6, 14));
        overview.WeekStartDate.Should().Be(new DateTime(2026, 6, 8));
        overview.WeekEndDate.Should().Be(new DateTime(2026, 6, 14));
    }

    [Fact]
    public async Task AddTaskToCurrentWeekAsync_RejectsForeignTask_AndPickerRejectsForeignProject()
    {
        var dbName = nameof(AddTaskToCurrentWeekAsync_RejectsForeignTask_AndPickerRejectsForeignProject);
        var ownerFixture = BuildFixture(dbName, userId: "owner");
        var intruderFixture = BuildFixture(dbName, userId: "intruder");

        var project = CreateProject("owner");
        var task = CreateTask(project, "owner", "Owned task");
        ownerFixture.Db.AddRange(project, task);
        await ownerFixture.Db.SaveChangesAsync();

        var addAct = () => intruderFixture.Week.AddTaskToCurrentWeekAsync(task.Id);
        var pickerAct = () => intruderFixture.Week.GetSelectableTasksAsync(project.Id);

        await addAct.Should().ThrowAsync<KeyNotFoundException>();
        await pickerAct.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AddTaskToCurrentWeekAsync_DoesNotMutateTaskPropertiesOrCurrentFocus()
    {
        var fixture = BuildFixture(nameof(AddTaskToCurrentWeekAsync_DoesNotMutateTaskPropertiesOrCurrentFocus));
        var project = CreateProject(DefaultUserId);
        var dueDate = DefaultToday.AddDays(3);
        var task = CreateTask(project, DefaultUserId, "Keep me stable", dueDate: dueDate, priority: TaskPriority.High, status: TaskItemStatus.Todo);
        fixture.Db.AddRange(project, task);
        await fixture.Db.SaveChangesAsync();

        await fixture.Week.AddTaskToCurrentWeekAsync(task.Id);

        var reloaded = await fixture.Db.Tasks.SingleAsync(candidate => candidate.Id == task.Id);
        var selection = await fixture.Db.WeeklyTaskSelections.SingleAsync();
        reloaded.DueDate.Should().Be(dueDate);
        reloaded.Priority.Should().Be(TaskPriority.High);
        reloaded.Status.Should().Be(TaskItemStatus.Todo);
        reloaded.IsCurrentTask.Should().BeFalse();
        selection.TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task AddTaskToCurrentWeekAsync_RejectsIneligibleTasks()
    {
        var fixture = BuildFixture(nameof(AddTaskToCurrentWeekAsync_RejectsIneligibleTasks));
        var activeProject = CreateProject(DefaultUserId, status: ProjectStatus.Active);
        var blockedProject = CreateProject(DefaultUserId, status: ProjectStatus.Blocked);
        var archivedProject = CreateProject(DefaultUserId, isArchived: true);
        var notStartedProject = CreateProject(DefaultUserId, status: ProjectStatus.NotStarted);
        var waitingTask = CreateTask(activeProject, DefaultUserId, "Waiting", status: TaskItemStatus.Waiting);
        var archivedTask = CreateTask(activeProject, DefaultUserId, "Archived", isArchived: true);
        var doneTask = CreateTask(activeProject, DefaultUserId, "Done", status: TaskItemStatus.Done);
        var parent = CreateTask(activeProject, DefaultUserId, "Parent");
        var subtask = CreateTask(activeProject, DefaultUserId, "Subtask", parentTaskId: parent.Id);
        var blockedProjectTask = CreateTask(blockedProject, DefaultUserId, "Blocked project");
        var archivedProjectTask = CreateTask(archivedProject, DefaultUserId, "Archived project");
        var notStartedProjectTask = CreateTask(notStartedProject, DefaultUserId, "Not started project");
        var dependencySource = CreateTask(activeProject, DefaultUserId, "Dependency source");
        var dependencyBlocked = CreateTask(activeProject, DefaultUserId, "Dependency blocked");

        fixture.Db.AddRange(
            activeProject, blockedProject, archivedProject, notStartedProject,
            waitingTask, archivedTask, doneTask, parent, subtask, blockedProjectTask, archivedProjectTask, notStartedProjectTask,
            dependencySource, dependencyBlocked,
            new TaskDependency
            {
                Id = Guid.NewGuid(),
                TaskId = dependencyBlocked.Id,
                DependsOnTaskId = dependencySource.Id
            });
        await fixture.Db.SaveChangesAsync();

        var invalidTaskIds = new[]
        {
            waitingTask.Id,
            archivedTask.Id,
            doneTask.Id,
            subtask.Id,
            blockedProjectTask.Id,
            archivedProjectTask.Id,
            notStartedProjectTask.Id,
            dependencyBlocked.Id
        };

        foreach (var taskId in invalidTaskIds)
        {
            var act = () => fixture.Week.AddTaskToCurrentWeekAsync(taskId);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        (await fixture.Db.WeeklyTaskSelections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddTaskToCurrentWeekAsync_IsIdempotent()
    {
        var fixture = BuildFixture(nameof(AddTaskToCurrentWeekAsync_IsIdempotent));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project, DefaultUserId, "Duplicate safe");
        fixture.Db.AddRange(project, task);
        await fixture.Db.SaveChangesAsync();

        await fixture.Week.AddTaskToCurrentWeekAsync(task.Id);
        await fixture.Week.AddTaskToCurrentWeekAsync(task.Id);

        (await fixture.Db.WeeklyTaskSelections.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveTaskFromCurrentWeekAsync_RemovesOnlyCurrentUsersCurrentWeekSelection()
    {
        var dbName = nameof(RemoveTaskFromCurrentWeekAsync_RemovesOnlyCurrentUsersCurrentWeekSelection);
        var ownerFixture = BuildFixture(dbName, userId: "owner");
        var otherFixture = BuildFixture(dbName, userId: "other");

        var ownerProject = CreateProject("owner");
        var ownerTask = CreateTask(ownerProject, "owner", "Owner task");
        var otherProject = CreateProject("other");
        var otherTask = CreateTask(otherProject, "other", "Other task");
        ownerFixture.Db.AddRange(ownerProject, ownerTask, otherProject, otherTask);
        await ownerFixture.Db.SaveChangesAsync();

        var monday = new DateTime(2026, 6, 15);
        ownerFixture.Db.WeeklyTaskSelections.AddRange(
            new WeeklyTaskSelection { Id = Guid.NewGuid(), UserId = "owner", TaskId = ownerTask.Id, WeekStartDate = monday },
            new WeeklyTaskSelection { Id = Guid.NewGuid(), UserId = "other", TaskId = otherTask.Id, WeekStartDate = monday },
            new WeeklyTaskSelection { Id = Guid.NewGuid(), UserId = "owner", TaskId = ownerTask.Id, WeekStartDate = monday.AddDays(-7) });
        await ownerFixture.Db.SaveChangesAsync();

        await ownerFixture.Week.RemoveTaskFromCurrentWeekAsync(ownerTask.Id);

        var selections = await ownerFixture.Db.WeeklyTaskSelections.AsNoTracking().ToListAsync();
        selections.Should().ContainSingle(selection => selection.UserId == "other" && selection.TaskId == otherTask.Id);
        selections.Should().ContainSingle(selection => selection.UserId == "owner" && selection.WeekStartDate == monday.AddDays(-7));
        selections.Should().NotContain(selection => selection.UserId == "owner" && selection.TaskId == ownerTask.Id && selection.WeekStartDate == monday);
    }

    [Fact]
    public async Task GetCurrentWeekOverviewAsync_TracksWeeklySelectionCountsWithoutTranslationFailure()
    {
        var fixture = BuildFixture(nameof(GetCurrentWeekOverviewAsync_TracksWeeklySelectionCountsWithoutTranslationFailure));
        var project = CreateProject(DefaultUserId, name: "Selected project");
        var selectedTask = CreateTask(project, DefaultUserId, "Selected this week");
        var unselectedTask = CreateTask(project, DefaultUserId, "Not selected");
        fixture.Db.AddRange(project, selectedTask, unselectedTask);
        fixture.Db.WeeklyTaskSelections.Add(new WeeklyTaskSelection
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            TaskId = selectedTask.Id,
            WeekStartDate = new DateTime(2026, 6, 15)
        });
        await fixture.Db.SaveChangesAsync();

        var overview = await fixture.Week.GetCurrentWeekOverviewAsync();

        overview.Projects.Should().ContainSingle(projectSummary => projectSummary.Id == project.Id);
        overview.Projects.Single(projectSummary => projectSummary.Id == project.Id).WeeklySelectionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentWeekOverviewAsync_KeepsCompletedSelections_AndShowsDueAttention()
    {
        var fixture = BuildFixture(nameof(GetCurrentWeekOverviewAsync_KeepsCompletedSelections_AndShowsDueAttention));
        var project = CreateProject(DefaultUserId);
        var selectedDone = CreateTask(project, DefaultUserId, "Done but selected", status: TaskItemStatus.Done, completedDate: DefaultToday.AddDays(-1));
        var overdueUnselected = CreateTask(project, DefaultUserId, "Overdue unselected", dueDate: DefaultToday.AddDays(-1));
        var parentWithDueSubtask = CreateTask(project, DefaultUserId, "Parent due by subtask");
        var dueSubtask = CreateTask(project, DefaultUserId, "Subtask due this week", dueDate: DefaultToday.AddDays(2), parentTaskId: parentWithDueSubtask.Id);
        fixture.Db.AddRange(project, selectedDone, overdueUnselected, parentWithDueSubtask, dueSubtask);
        fixture.Db.WeeklyTaskSelections.Add(new WeeklyTaskSelection
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            TaskId = selectedDone.Id,
            WeekStartDate = new DateTime(2026, 6, 15)
        });
        await fixture.Db.SaveChangesAsync();

        var overview = await fixture.Week.GetCurrentWeekOverviewAsync();

        overview.SelectedTaskGroups.SelectMany(group => group.Tasks)
            .Should().Contain(task => task.Id == selectedDone.Id && task.Status == TaskItemStatus.Done);
        overview.OverdueAttention.Should().Contain(task => task.Id == overdueUnselected.Id);
        overview.DueThisWeekAttention.Should().Contain(task => task.Id == parentWithDueSubtask.Id);
    }

    [Fact]
    public async Task UpdateProjectStatusAsync_MovesSelectedTaskIntoNeedsReplanning()
    {
        var fixture = BuildFixture(nameof(UpdateProjectStatusAsync_MovesSelectedTaskIntoNeedsReplanning));
        var project = CreateProject(DefaultUserId);
        var task = CreateTask(project, DefaultUserId, "Selected task");
        fixture.Db.AddRange(project, task);
        await fixture.Db.SaveChangesAsync();
        await fixture.Week.AddTaskToCurrentWeekAsync(task.Id);

        var initialOverview = await fixture.Week.GetCurrentWeekOverviewAsync();
        var projectCard = initialOverview.Projects.Single(candidate => candidate.Id == project.Id);

        await fixture.Week.UpdateProjectStatusAsync(new WeekProjectStatusUpdateDto(project.Id, ProjectStatus.Parked, projectCard.RowVersion));

        var refreshed = await fixture.Week.GetCurrentWeekOverviewAsync();
        refreshed.NeedsReplanning.Should().ContainSingle(candidate => candidate.Id == task.Id);
        (await fixture.Db.WeeklyTaskSelections.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CarryForwardTasksAsync_IsExplicitEligibleHistoryPreservingAndIdempotent()
    {
        var fixture = BuildFixture(nameof(CarryForwardTasksAsync_IsExplicitEligibleHistoryPreservingAndIdempotent));
        var project = CreateProject(DefaultUserId);
        var eligible = CreateTask(project, DefaultUserId, "Carry me");
        var ineligible = CreateTask(project, DefaultUserId, "Waiting carry", status: TaskItemStatus.Waiting);
        fixture.Db.AddRange(project, eligible, ineligible);
        fixture.Db.WeeklyTaskSelections.AddRange(
            new WeeklyTaskSelection { Id = Guid.NewGuid(), UserId = DefaultUserId, TaskId = eligible.Id, WeekStartDate = new DateTime(2026, 6, 8) },
            new WeeklyTaskSelection { Id = Guid.NewGuid(), UserId = DefaultUserId, TaskId = ineligible.Id, WeekStartDate = new DateTime(2026, 6, 8) });
        await fixture.Db.SaveChangesAsync();

        var candidates = await fixture.Week.GetCarryForwardCandidatesAsync();
        candidates.Should().ContainSingle(candidate => candidate.Task.Id == eligible.Id && candidate.CanCarryForward);
        candidates.Should().ContainSingle(candidate => candidate.Task.Id == ineligible.Id && !candidate.CanCarryForward);

        await fixture.Week.CarryForwardTasksAsync([eligible.Id]);
        await fixture.Week.CarryForwardTasksAsync([eligible.Id]);

        var selections = await fixture.Db.WeeklyTaskSelections.AsNoTracking().OrderBy(selection => selection.WeekStartDate).ToListAsync();
        selections.Should().Contain(selection => selection.TaskId == eligible.Id && selection.WeekStartDate == new DateTime(2026, 6, 8));
        selections.Should().ContainSingle(selection => selection.TaskId == eligible.Id && selection.WeekStartDate == new DateTime(2026, 6, 15));

        var carryIneligible = () => fixture.Week.CarryForwardTasksAsync([ineligible.Id]);
        await carryIneligible.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateProjectStatusAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var fixture = BuildFixture(nameof(UpdateProjectStatusAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var project = CreateProject(DefaultUserId);
        fixture.Db.Projects.Add(project);
        await fixture.Db.SaveChangesAsync();

        var act = () => fixture.Week.UpdateProjectStatusAsync(
            new WeekProjectStatusUpdateDto(project.Id, ProjectStatus.Blocked, [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private static TestFixture BuildFixture(
        string dbName,
        string userId = DefaultUserId,
        DateTime? today = null,
        Action<IServiceCollection>? configureServices = null,
        bool useFakeTimeZoneService = true)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(options => options.UseInMemoryDatabase(dbName, DatabaseRoot));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset((today ?? DefaultToday).Date.AddHours(12), TimeSpan.Zero)));
        services.AddBrainyApplication();

        if (useFakeTimeZoneService)
            services.AddSingleton<IUserTimeZoneService>(new FakeUserTimeZoneService(today ?? DefaultToday));

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return new TestFixture(
            provider.GetRequiredService<IWeekService>(),
            provider.GetRequiredService<BrainyDbContext>(),
            provider);
    }

    private static Project CreateProject(
        string userId,
        ProjectStatus status = ProjectStatus.Active,
        bool isArchived = false,
        string? name = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name ?? $"Project {Guid.NewGuid():N}",
            Status = status,
            Priority = ProjectPriority.High,
            IsArchived = isArchived
        };

    private static TaskItem CreateTask(
        Project project,
        string userId,
        string title,
        TaskItemStatus status = TaskItemStatus.Todo,
        TaskPriority priority = TaskPriority.Medium,
        DateTime? dueDate = null,
        bool isArchived = false,
        Guid? parentTaskId = null,
        DateTime? completedDate = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = project.Id,
            Project = project,
            Title = title,
            Status = status,
            Priority = priority,
            DueDate = dueDate,
            IsArchived = isArchived,
            ParentTaskId = parentTaskId,
            CompletedDate = completedDate
        };

    private static string? FindPreferredTimeZoneId()
    {
        var candidates = new[]
        {
            "Pacific Standard Time",
            "America/Los_Angeles"
        };

        return candidates.FirstOrDefault(candidate => TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _));
    }

    private sealed record TestFixture(IWeekService Week, BrainyDbContext Db, ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FakeUserTimeZoneService(DateTime today) : IUserTimeZoneService
    {
        public Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
        public Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult(TimeZoneInfo.Utc);
        public Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today.Date);
        public Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetTimeZoneOverrideIdAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetTimeZoneOverrideAsync(string timeZoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(DateTime localStartDate, DateTime localEndDate, CancellationToken cancellationToken = default)
            => Task.FromResult((localStartDate.Date, localEndDate.Date.AddDays(1)));
    }
}
