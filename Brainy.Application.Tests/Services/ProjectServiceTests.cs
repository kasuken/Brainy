using Brainy.Application.Common;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Identity;
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
/// Unit tests for <see cref="IProjectService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class ProjectServiceTests
{
    private const string DefaultUserId = "test-user-1";

    private static (IProjectService Projects, IAreaService Areas) BuildServices(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IProjectService>(), provider.GetRequiredService<IAreaService>());
    }

    private static (IProjectService Projects, BrainyDbContext Db) BuildLifecycleServices(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(DefaultUserId));
        services.AddBrainyApplication();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IProjectService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidArea_ReturnsDtoWithAreaId()
    {
        var (projects, areas) = BuildServices(nameof(CreateAsync_WithValidArea_ReturnsDtoWithAreaId));

        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        var result = await projects.CreateAsync(new CreateProjectDto("Launch website", area.Id));

        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("Launch website");
        result.AreaId.Should().Be(area.Id);
    }

    [Fact]
    public async Task CreateAsync_WithNonExistentAreaId_ThrowsKeyNotFoundException()
    {
        var (projects, _) = BuildServices(nameof(CreateAsync_WithNonExistentAreaId_ThrowsKeyNotFoundException));

        var nonExistentAreaId = Guid.NewGuid();

        var act = () => projects.CreateAsync(new CreateProjectDto("My project", nonExistentAreaId));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_WithAreaBelongingToAnotherUser_ThrowsKeyNotFoundException()
    {
        var dbName = nameof(CreateAsync_WithAreaBelongingToAnotherUser_ThrowsKeyNotFoundException);
        var (_, areasUser2) = BuildServices(dbName, "user-2");
        var (projectsUser1, _) = BuildServices(dbName, "user-1");

        // user-2 creates an area
        var area = await areasUser2.CreateAsync(new CreateAreaDto("User2 Area"));

        // user-1 tries to create a project under user-2's area
        var act = () => projectsUser1.CreateAsync(new CreateProjectDto("Stolen project", area.Id));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithValidArea_PersistsNewAreaId()
    {
        var (projects, areas) = BuildServices(nameof(UpdateAsync_WithValidArea_PersistsNewAreaId));

        var area1 = await areas.CreateAsync(new CreateAreaDto("Area One"));
        var area2 = await areas.CreateAsync(new CreateAreaDto("Area Two"));
        var project = await projects.CreateAsync(new CreateProjectDto("My project", area1.Id));

        var updated = await projects.UpdateAsync(new UpdateProjectDto(
            project.Id, "My project", area2.Id, null, null,
            ProjectStatus.Active, ProjectPriority.High, null, null));

        updated.AreaId.Should().Be(area2.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentAreaId_ThrowsKeyNotFoundException()
    {
        var (projects, areas) = BuildServices(nameof(UpdateAsync_WithNonExistentAreaId_ThrowsKeyNotFoundException));

        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var project = await projects.CreateAsync(new CreateProjectDto("My project", area.Id));

        var act = () => projects.UpdateAsync(new UpdateProjectDto(
            project.Id, "My project", Guid.NewGuid(), null, null,
            ProjectStatus.Active, ProjectPriority.Medium, null, null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (projects, areas) = BuildServices(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));
        var created = await projects.CreateAsync(new CreateProjectDto("My project", area.Id));

        // The InMemory provider validates concurrency tokens but does not regenerate
        // them on update, so a mismatched token simulates the stale value a second
        // tab would hold after SQL Server bumps the rowversion.
        var act = () => projects.UpdateAsync(new UpdateProjectDto(
            created.Id, "My stale edit", area.Id, null, null,
            ProjectStatus.Active, ProjectPriority.Medium, null, null,
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task ArchiveAndRestoreAsync_PreservesProjectStatusAndManualTaskArchive()
    {
        var (projects, db) = BuildLifecycleServices(nameof(ArchiveAndRestoreAsync_PreservesProjectStatusAndManualTaskArchive));
        var project = new Project
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Lifecycle",
            Status = ProjectStatus.Active, Priority = ProjectPriority.Medium,
        };
        var manuallyArchived = new TaskItem
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, ProjectId = project.Id,
            Title = "Already archived", Status = TaskItemStatus.Todo,
            IsArchived = true, ArchivedAtUtc = DateTime.UtcNow.AddDays(-2),
            ArchiveOperationId = Guid.NewGuid(),
        };
        var active = new TaskItem
        {
            Id = Guid.NewGuid(), UserId = DefaultUserId, ProjectId = project.Id,
            Title = "Active", Status = TaskItemStatus.InProgress, IsCurrentTask = true,
        };
        db.Projects.Add(project);
        db.Tasks.AddRange(manuallyArchived, active);
        await db.SaveChangesAsync();

        await projects.ArchiveAsync(project.Id);
        await projects.RestoreAsync(project.Id);

        var restoredProject = await db.Projects.AsNoTracking().SingleAsync();
        var tasks = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id);
        restoredProject.Status.Should().Be(ProjectStatus.Active);
        restoredProject.StatusBeforeArchive.Should().BeNull();
        tasks[manuallyArchived.Id].IsArchived.Should().BeTrue();
        tasks[active.Id].IsArchived.Should().BeFalse();
        tasks[active.Id].IsCurrentTask.Should().BeFalse();
    }

    [Fact]
    public async Task ArchiveAndRestoreAsync_PersistsAndClearsArchivedReason()
    {
        var (projects, db) = BuildLifecycleServices(nameof(ArchiveAndRestoreAsync_PersistsAndClearsArchivedReason));
        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Name = "Lifecycle",
            Status = ProjectStatus.Active,
            Priority = ProjectPriority.Medium
        };
        var active = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            ProjectId = project.Id,
            Title = "Active",
            Status = TaskItemStatus.Todo
        };
        db.Projects.Add(project);
        db.Tasks.Add(active);
        await db.SaveChangesAsync();

        await projects.ArchiveAsync(project.Id, archivedReason: "Completed and documented");

        var archivedProject = await db.Projects.AsNoTracking().SingleAsync();
        var archivedTask = await db.Tasks.AsNoTracking().SingleAsync();
        archivedProject.ArchivedReason.Should().Be("Completed and documented");
        archivedTask.ArchivedReason.Should().Be("Completed and documented");

        await projects.RestoreAsync(project.Id);

        var restoredProject = await db.Projects.AsNoTracking().SingleAsync();
        var restoredTask = await db.Tasks.AsNoTracking().SingleAsync();
        restoredProject.ArchivedReason.Should().BeNull();
        restoredTask.ArchivedReason.Should().BeNull();
    }
}
