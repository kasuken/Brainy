using AwesomeAssertions;
using Brainy.Application.DTOs.Calendar;
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
/// Covers <see cref="ICalendarFeedService"/>: which of a user's tasks/projects/milestones
/// become feed events, that archived and completed/done items are excluded by default (see
/// AGENTS.md), that <see cref="CalendarFilterDto"/> narrows results, and — the acceptance
/// criterion called out explicitly in issue #314 — that one user's events never include
/// another user's data.
/// </summary>
public class CalendarFeedServiceTests
{
    private const string UserA = "feed-user-a";
    private const string UserB = "feed-user-b";

    private static (ICalendarFeedService sut, BrainyDbContext db) BuildService(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserA));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ICalendarFeedService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(
        string userId,
        string name = "Project",
        bool isArchived = false,
        ProjectStatus status = ProjectStatus.Active,
        DateTime? dueDate = null,
        Guid? areaId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = name,
        Status = status,
        Priority = ProjectPriority.Medium,
        IsArchived = isArchived,
        DueDate = dueDate,
        AreaId = areaId
    };

    private static TaskItem CreateTask(
        string userId,
        Guid projectId,
        string title = "Task",
        DateTime? dueDate = null,
        bool isArchived = false,
        TaskItemStatus status = TaskItemStatus.Todo,
        TaskPriority priority = TaskPriority.Medium) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ProjectId = projectId,
        Title = title,
        Status = status,
        Priority = priority,
        DueDate = dueDate,
        IsArchived = isArchived
    };

    private static Goal CreateGoal(string userId, bool isArchived = false, Guid? areaId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = "Goal",
        IsArchived = isArchived,
        AreaId = areaId
    };

    private static GoalMilestone CreateMilestone(
        Guid goalId,
        string title = "Milestone",
        DateTime? dueDate = null,
        bool isCompleted = false) => new()
    {
        Id = Guid.NewGuid(),
        GoalId = goalId,
        Title = title,
        DueDate = dueDate,
        IsCompleted = isCompleted
    };

    [Fact]
    public async Task GetFeedEventsAsync_IncludesTasksProjectsAndMilestonesWithDueDates()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_IncludesTasksProjectsAndMilestonesWithDueDates));
        var project = CreateProject(UserA, dueDate: new DateTime(2026, 3, 20));
        var task = CreateTask(UserA, project.Id, dueDate: new DateTime(2026, 3, 10));
        var goal = CreateGoal(UserA);
        var milestone = CreateMilestone(goal.Id, dueDate: new DateTime(2026, 3, 15));
        db.Projects.Add(project);
        db.Tasks.Add(task);
        db.Goals.Add(goal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        var events = await sut.GetFeedEventsAsync(UserA);

        events.Should().HaveCount(3);
        events.Should().ContainSingle(e => e.Kind == CalendarFeedEventKind.Task && e.SourceId == task.Id);
        events.Should().ContainSingle(e => e.Kind == CalendarFeedEventKind.ProjectDeadline && e.SourceId == project.Id);
        events.Should().ContainSingle(e => e.Kind == CalendarFeedEventKind.GoalMilestone && e.SourceId == milestone.Id);
    }

    [Fact]
    public async Task GetFeedEventsAsync_ExcludesItemsWithoutADueDate()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_ExcludesItemsWithoutADueDate));
        var project = CreateProject(UserA, dueDate: null);
        db.Projects.Add(project);
        db.Tasks.Add(CreateTask(UserA, project.Id, dueDate: null));
        await db.SaveChangesAsync();

        var events = await sut.GetFeedEventsAsync(UserA);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeedEventsAsync_ExcludesArchivedTasksProjectsAndGoals()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_ExcludesArchivedTasksProjectsAndGoals));

        var archivedProject = CreateProject(UserA, dueDate: new DateTime(2026, 4, 1), isArchived: true);
        var activeProject = CreateProject(UserA, dueDate: null); // no deadline of its own; only hosts the archived task below
        var archivedTaskUnderActiveProject = CreateTask(UserA, activeProject.Id, dueDate: new DateTime(2026, 4, 3), isArchived: true);
        var archivedGoal = CreateGoal(UserA, isArchived: true);
        var milestoneUnderArchivedGoal = CreateMilestone(archivedGoal.Id, dueDate: new DateTime(2026, 4, 4));

        db.Projects.AddRange(archivedProject, activeProject);
        db.Tasks.Add(archivedTaskUnderActiveProject);
        db.Goals.Add(archivedGoal);
        db.GoalMilestones.Add(milestoneUnderArchivedGoal);
        await db.SaveChangesAsync();

        var events = await sut.GetFeedEventsAsync(UserA);

        events.Should().BeEmpty("an archived project, an archived task, and a milestone under an archived goal must all be excluded by default");
    }

    [Fact]
    public async Task GetFeedEventsAsync_ExcludesCompletedProjectsDoneTasksAndCompletedMilestones()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_ExcludesCompletedProjectsDoneTasksAndCompletedMilestones));

        var completedProject = CreateProject(UserA, dueDate: new DateTime(2026, 5, 1), status: ProjectStatus.Completed);
        var activeProject = CreateProject(UserA, dueDate: null); // no deadline of its own; only hosts the done task below
        var doneTask = CreateTask(UserA, activeProject.Id, dueDate: new DateTime(2026, 5, 3), status: TaskItemStatus.Done);
        var goal = CreateGoal(UserA);
        var completedMilestone = CreateMilestone(goal.Id, dueDate: new DateTime(2026, 5, 4), isCompleted: true);

        db.Projects.AddRange(completedProject, activeProject);
        db.Tasks.Add(doneTask);
        db.Goals.Add(goal);
        db.GoalMilestones.Add(completedMilestone);
        await db.SaveChangesAsync();

        var events = await sut.GetFeedEventsAsync(UserA);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeedEventsAsync_FiltersTasksByProjectPriorityStatusAndSearchTerm()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_FiltersTasksByProjectPriorityStatusAndSearchTerm));
        var targetProject = CreateProject(UserA, name: "Target");
        var otherProject = CreateProject(UserA, name: "Other");
        var match = CreateTask(UserA, targetProject.Id, title: "Deploy release", dueDate: new DateTime(2026, 6, 1), priority: TaskPriority.High, status: TaskItemStatus.InProgress);
        var wrongProject = CreateTask(UserA, otherProject.Id, title: "Deploy release", dueDate: new DateTime(2026, 6, 1), priority: TaskPriority.High, status: TaskItemStatus.InProgress);
        var wrongPriority = CreateTask(UserA, targetProject.Id, title: "Deploy release", dueDate: new DateTime(2026, 6, 1), priority: TaskPriority.Low, status: TaskItemStatus.InProgress);

        db.Projects.AddRange(targetProject, otherProject);
        db.Tasks.AddRange(match, wrongProject, wrongPriority);
        await db.SaveChangesAsync();

        var filter = new CalendarFilterDto(
            ProjectId: targetProject.Id,
            Priority: TaskPriority.High,
            Status: TaskItemStatus.InProgress,
            SearchTerm: "deploy");

        var events = await sut.GetFeedEventsAsync(UserA, filter);

        events.Should().ContainSingle().Which.SourceId.Should().Be(match.Id);
    }

    [Fact]
    public async Task GetFeedEventsAsync_FiltersProjectsAndMilestonesByArea()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_FiltersProjectsAndMilestonesByArea));
        var targetArea = new Area { Id = Guid.NewGuid(), UserId = UserA, Name = "Target area" };
        var otherArea = new Area { Id = Guid.NewGuid(), UserId = UserA, Name = "Other area" };
        var projectInTargetArea = CreateProject(UserA, dueDate: new DateTime(2026, 7, 1), areaId: targetArea.Id);
        var projectInOtherArea = CreateProject(UserA, dueDate: new DateTime(2026, 7, 2), areaId: otherArea.Id);
        var goalInTargetArea = CreateGoal(UserA, areaId: targetArea.Id);
        var milestoneInTargetArea = CreateMilestone(goalInTargetArea.Id, dueDate: new DateTime(2026, 7, 3));

        db.Areas.AddRange(targetArea, otherArea);
        db.Projects.AddRange(projectInTargetArea, projectInOtherArea);
        db.Goals.Add(goalInTargetArea);
        db.GoalMilestones.Add(milestoneInTargetArea);
        await db.SaveChangesAsync();

        var events = await sut.GetFeedEventsAsync(UserA, new CalendarFilterDto(AreaId: targetArea.Id));

        events.Should().HaveCount(2);
        events.Should().Contain(e => e.SourceId == projectInTargetArea.Id);
        events.Should().Contain(e => e.SourceId == milestoneInTargetArea.Id);
    }

    [Fact]
    public async Task GetFeedEventsAsync_NeverReturnsAnotherUsersEvents()
    {
        var (sut, db) = BuildService(nameof(GetFeedEventsAsync_NeverReturnsAnotherUsersEvents));

        var projectA = CreateProject(UserA, dueDate: new DateTime(2026, 8, 1));
        var taskA = CreateTask(UserA, projectA.Id, dueDate: new DateTime(2026, 8, 1));
        var goalA = CreateGoal(UserA);
        var milestoneA = CreateMilestone(goalA.Id, dueDate: new DateTime(2026, 8, 1));

        var projectB = CreateProject(UserB, dueDate: new DateTime(2026, 8, 1));
        var taskB = CreateTask(UserB, projectB.Id, dueDate: new DateTime(2026, 8, 1));
        var goalB = CreateGoal(UserB);
        var milestoneB = CreateMilestone(goalB.Id, dueDate: new DateTime(2026, 8, 1));

        db.Projects.AddRange(projectA, projectB);
        db.Tasks.AddRange(taskA, taskB);
        db.Goals.AddRange(goalA, goalB);
        db.GoalMilestones.AddRange(milestoneA, milestoneB);
        await db.SaveChangesAsync();

        var eventsForA = await sut.GetFeedEventsAsync(UserA);
        var eventsForB = await sut.GetFeedEventsAsync(UserB);

        eventsForA.Select(e => e.SourceId).Should().BeEquivalentTo([projectA.Id, taskA.Id, milestoneA.Id]);
        eventsForB.Select(e => e.SourceId).Should().BeEquivalentTo([projectB.Id, taskB.Id, milestoneB.Id]);
        eventsForA.Select(e => e.SourceId).Should().NotIntersectWith(eventsForB.Select(e => e.SourceId));
    }
}
