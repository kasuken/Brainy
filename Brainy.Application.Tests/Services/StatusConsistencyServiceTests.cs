using Brainy.Application.DTOs.Today;
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
/// Unit tests for <see cref="IStatusConsistencyService"/> resolved via the real DI
/// container with an EF Core InMemory database. Each test uses a unique database name.
/// </summary>
public class StatusConsistencyServiceTests
{
    private const string DefaultUserId = "u1";

    private static (IStatusConsistencyService sut, BrainyDbContext db) BuildService(
        string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IStatusConsistencyService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(
        string userId,
        ProjectStatus status,
        string name = "Test Project",
        bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Status = status,
            IsArchived = isArchived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static TaskItem CreateTask(
        Guid projectId,
        string userId,
        TaskItemStatus status,
        bool isArchived = false)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = projectId,
            Title = "Test Task",
            Status = status,
            IsArchived = isArchived,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    // ── The canonical case: in-progress work in a non-active project ─────────────

    [Theory]
    [InlineData(ProjectStatus.NotStarted)]
    [InlineData(ProjectStatus.Blocked)]
    [InlineData(ProjectStatus.Parked)]
    public async Task InProgressTask_InNonActiveProject_IsFlagged(ProjectStatus status)
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, status);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.InProgress));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().ContainSingle()
            .Which.Kind.Should().Be(StatusConsistencyKind.InProgressWorkInInactiveProject);
        warnings[0].Count.Should().Be(1);
        warnings[0].Severity.Should().Be(StatusConsistencySeverity.Warning);
    }

    [Fact]
    public async Task InProgressTask_InActiveProject_IsNotFlagged()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Active);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.InProgress));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    // ── Completed project with open tasks ───────────────────────────────────────

    [Fact]
    public async Task CompletedProject_WithOpenTask_IsFlagged()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Completed);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Todo));
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Done));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().ContainSingle()
            .Which.Kind.Should().Be(StatusConsistencyKind.CompletedProjectWithOpenTasks);
        warnings[0].Count.Should().Be(1); // only the Todo counts as open
    }

    [Fact]
    public async Task CompletedProject_AllTasksDone_IsNotFlagged()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Completed);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Done));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    // ── Archived project with open tasks ────────────────────────────────────────

    [Fact]
    public async Task ArchivedProject_WithOpenTask_IsFlagged()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Active, isArchived: true);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.InProgress));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().ContainSingle()
            .Which.Kind.Should().Be(StatusConsistencyKind.ArchivedProjectWithOpenTasks);
    }

    // ── Active project whose work is all done ───────────────────────────────────

    [Fact]
    public async Task ActiveProject_AllTasksDone_IsFlaggedAsInfo()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Active);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Done));
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Done));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().ContainSingle();
        warnings[0].Kind.Should().Be(StatusConsistencyKind.ActiveProjectAllTasksDone);
        warnings[0].Severity.Should().Be(StatusConsistencySeverity.Info);
        warnings[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task ActiveProject_WithNoTasks_IsNotFlagged()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        ctx.Projects.Add(CreateProject(DefaultUserId, ProjectStatus.Active));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    // ── Not-started project that already has completed work ─────────────────────

    [Fact]
    public async Task NotStartedProject_WithDoneWork_IsFlaggedAsInfo()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.NotStarted);
        ctx.Projects.Add(project);
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Done));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().ContainSingle();
        warnings[0].Kind.Should().Be(StatusConsistencyKind.NotStartedProjectWithProgress);
        warnings[0].Severity.Should().Be(StatusConsistencySeverity.Info);
    }

    // ── Noise control ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchivedTasks_DoNotCountAsInconsistent()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var project = CreateProject(DefaultUserId, ProjectStatus.Parked);
        ctx.Projects.Add(project);
        // An archived task, and one carrying the Archived status, are both closed work.
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.InProgress, isArchived: true));
        ctx.Tasks.Add(CreateTask(project.Id, DefaultUserId, TaskItemStatus.Archived));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyAnotherUsersProjectIsInconsistent_ReturnsNothing()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var otherProject = CreateProject("someone-else", ProjectStatus.Blocked);
        ctx.Projects.Add(otherProject);
        ctx.Tasks.Add(CreateTask(otherProject.Id, "someone-else", TaskItemStatus.InProgress));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsistentWorkspace_ReturnsNoWarnings()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        var active = CreateProject(DefaultUserId, ProjectStatus.Active, "Active");
        var completed = CreateProject(DefaultUserId, ProjectStatus.Completed, "Completed");
        ctx.Projects.AddRange(active, completed);
        ctx.Tasks.Add(CreateTask(active.Id, DefaultUserId, TaskItemStatus.InProgress));
        ctx.Tasks.Add(CreateTask(active.Id, DefaultUserId, TaskItemStatus.Todo));
        ctx.Tasks.Add(CreateTask(completed.Id, DefaultUserId, TaskItemStatus.Done));
        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().BeEmpty();
    }

    // ── Ordering ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Warnings_AreOrderedBySeverityThenName()
    {
        var db = Guid.NewGuid().ToString();
        var (sut, ctx) = BuildService(db);

        // Info-level nudge.
        var info = CreateProject(DefaultUserId, ProjectStatus.NotStarted, "Zeta");
        ctx.Projects.Add(info);
        ctx.Tasks.Add(CreateTask(info.Id, DefaultUserId, TaskItemStatus.Done));

        // Warning-level contradiction.
        var warn = CreateProject(DefaultUserId, ProjectStatus.Blocked, "Alpha");
        ctx.Projects.Add(warn);
        ctx.Tasks.Add(CreateTask(warn.Id, DefaultUserId, TaskItemStatus.InProgress));

        await ctx.SaveChangesAsync();

        var warnings = await sut.GetWarningsAsync();

        warnings.Should().HaveCount(2);
        warnings[0].Severity.Should().Be(StatusConsistencySeverity.Warning); // warning before info
        warnings[1].Severity.Should().Be(StatusConsistencySeverity.Info);
    }
}
