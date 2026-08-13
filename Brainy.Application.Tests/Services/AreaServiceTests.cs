using Brainy.Application.DTOs.Areas;
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
/// Unit tests for <see cref="IAreaService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class AreaServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IAreaService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IAreaService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Area CreateArea(string userId, string name = "Area", bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = name, IsArchived = isArchived };

    private static Project CreateProject(string userId, Guid areaId, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "P", AreaId = areaId, IsArchived = isArchived };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_PersistsAreaForCurrentUser()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithValidDto_PersistsAreaForCurrentUser));

        var result = await sut.CreateAsync(new CreateAreaDto("  Health  ", "desc", "purpose"));

        var stored = await db.Areas.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.UserId.Should().Be(DefaultUserId);
        stored.Name.Should().Be("Health");
    }

    [Fact]
    public async Task CreateAsync_WithBlankName_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithBlankName_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateAreaDto("   "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveAsync_ExcludesArchivedAreas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesArchivedAreas));
        db.Areas.Add(CreateArea(DefaultUserId, "Active"));
        db.Areas.Add(CreateArea(DefaultUserId, "Archived", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherUsersAreas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesOtherUsersAreas));
        db.Areas.Add(CreateArea(OtherUserId, "Foreign"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllArchivedAsync_ReturnsOnlyArchivedAreas()
    {
        var (sut, db) = BuildService(nameof(GetAllArchivedAsync_ReturnsOnlyArchivedAreas));
        db.Areas.Add(CreateArea(DefaultUserId, "Active"));
        db.Areas.Add(CreateArea(DefaultUserId, "Archived", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetAllArchivedAsync();

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Archived");
    }

    [Fact]
    public async Task GetByIdAsync_WhenAreaBelongsToAnotherUser_ReturnsNull()
    {
        var (sut, db) = BuildService(nameof(GetByIdAsync_WhenAreaBelongsToAnotherUser_ReturnsNull));
        var foreign = CreateArea(OtherUserId);
        db.Areas.Add(foreign);
        await db.SaveChangesAsync();

        var result = await sut.GetByIdAsync(foreign.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_CountsOnlyActiveProjects()
    {
        var (sut, db) = BuildService(nameof(GetDetailAsync_CountsOnlyActiveProjects));
        var area = CreateArea(DefaultUserId);
        db.Areas.Add(area);
        db.Projects.Add(CreateProject(DefaultUserId, area.Id));
        db.Projects.Add(CreateProject(DefaultUserId, area.Id, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetDetailAsync(area.Id);

        result.Should().NotBeNull();
        result!.ActiveProjectCount.Should().Be(1);
    }

    // ── Archive / Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_SetsArchivedFlagAndTimestamp));
        var area = CreateArea(DefaultUserId);
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(area.Id);

        var stored = await db.Areas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeTrue();
        stored.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveAsync_WithActiveChildren_ReportsEveryBlockerAndLeavesAreaActive()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_WithActiveChildren_ReportsEveryBlockerAndLeavesAreaActive));
        var area = CreateArea(DefaultUserId, "Work");
        db.Areas.Add(area);
        db.Projects.Add(CreateProject(DefaultUserId, area.Id));
        db.Resources.Add(new Resource { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Reference", AreaId = area.Id });
        db.Goals.Add(new Goal { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Goal", AreaId = area.Id });
        db.Notes.Add(new Note { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Note", Content = "body", AreaId = area.Id });
        db.Ideas.Add(new Idea { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Idea", AreaId = area.Id });
        db.Outputs.Add(new Output { Id = Guid.NewGuid(), UserId = DefaultUserId, Title = "Output", Content = "body", AreaId = area.Id });
        await db.SaveChangesAsync();

        var act = () => sut.ArchiveAsync(area.Id);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().ContainAll(
            "1 active project", "1 active resource", "1 active goal",
            "1 active note", "1 active idea", "1 active output");
        (await db.Areas.AsNoTracking().SingleAsync()).IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_ClearsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_ClearsArchivedFlagAndTimestamp));
        var area = CreateArea(DefaultUserId, isArchived: true);
        area.ArchivedAtUtc = DateTime.UtcNow;
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        await sut.RestoreAsync(area.Id);

        var stored = await db.Areas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeFalse();
        stored.ArchivedAtUtc.Should().BeNull();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenAreaHasActiveProjects_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenAreaHasActiveProjects_ThrowsInvalidOperationException));
        var area = CreateArea(DefaultUserId);
        db.Areas.Add(area);
        db.Projects.Add(CreateProject(DefaultUserId, area.Id));
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(area.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_WhenAreaHasOnlyArchivedProjects_RemovesArea()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenAreaHasOnlyArchivedProjects_RemovesArea));
        var area = CreateArea(DefaultUserId);
        db.Areas.Add(area);
        db.Projects.Add(CreateProject(DefaultUserId, area.Id, isArchived: true));
        await db.SaveChangesAsync();

        await sut.DeleteAsync(area.Id);

        (await db.Areas.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenAreaBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenAreaBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateArea(OtherUserId);
        db.Areas.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(foreign.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Project linking ───────────────────────────────────────────────────────

    [Fact]
    public async Task LinkProjectAsync_AssignsProjectToArea()
    {
        var (sut, db) = BuildService(nameof(LinkProjectAsync_AssignsProjectToArea));
        var oldArea = CreateArea(DefaultUserId, "Old");
        var newArea = CreateArea(DefaultUserId, "New");
        var project = CreateProject(DefaultUserId, oldArea.Id);
        db.Areas.AddRange(oldArea, newArea);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await sut.LinkProjectAsync(newArea.Id, project.Id);

        var stored = await db.Projects.AsNoTracking().SingleAsync();
        stored.AreaId.Should().Be(newArea.Id);
    }

    [Fact]
    public async Task LinkProjectAsync_ToArchivedArea_IsRejected()
    {
        var (sut, db) = BuildService(nameof(LinkProjectAsync_ToArchivedArea_IsRejected));
        var activeArea = CreateArea(DefaultUserId, "Active");
        var archivedArea = CreateArea(DefaultUserId, "Archived", isArchived: true);
        var project = CreateProject(DefaultUserId, activeArea.Id);
        db.Areas.AddRange(activeArea, archivedArea);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var act = () => sut.LinkProjectAsync(archivedArea.Id, project.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Projects.AsNoTracking().SingleAsync()).AreaId.Should().Be(activeArea.Id);
    }

    [Fact]
    public async Task UnlinkProjectAsync_AlwaysThrows_BecauseProjectsRequireAnArea()
    {
        // Pins the product invariant: a project must always belong to an area.
        var (sut, _) = BuildService(nameof(UnlinkProjectAsync_AlwaysThrows_BecauseProjectsRequireAnArea));

        var act = () => sut.UnlinkProjectAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
