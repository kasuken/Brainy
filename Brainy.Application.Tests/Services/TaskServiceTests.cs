using Brainy.Application.DTOs.Tasks;
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
}
