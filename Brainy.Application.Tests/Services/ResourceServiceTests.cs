using Brainy.Application.Common;
using Brainy.Application.DTOs.Resources;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IResourceService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Search terms use exact casing because the InMemory provider compares ordinally.
/// </summary>
public class ResourceServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IResourceService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IResourceService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Resource CreateResource(
        string userId,
        string name = "Resource",
        string? topic = null,
        bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = name, Topic = topic, IsArchived = isArchived };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_PersistsResourceForCurrentUser()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithValidDto_PersistsResourceForCurrentUser));

        var result = await sut.CreateAsync(new CreateResourceDto("Design patterns", Topic: "engineering"));

        var stored = await db.Resources.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.UserId.Should().Be(DefaultUserId);
        stored.Topic.Should().Be("engineering");
    }

    [Fact]
    public async Task CreateAsync_WithForeignArea_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithForeignArea_ThrowsKeyNotFoundException));
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        db.Areas.Add(foreignArea);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateResourceDto("Resource", AreaId: foreignArea.Id));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Resources.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithOwnedArea_PersistsAreaLink()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithOwnedArea_PersistsAreaLink));
        var area = new Area { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Owned area" };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateResourceDto("Resource", AreaId: area.Id));

        result.AreaId.Should().Be(area.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithForeignArea_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_WithForeignArea_ThrowsKeyNotFoundException));
        var resource = CreateResource(DefaultUserId);
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        db.AddRange(resource, foreignArea);
        await db.SaveChangesAsync();

        var act = () => sut.UpdateAsync(new UpdateResourceDto(
            resource.Id, resource.Name, null, null, foreignArea.Id, null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (sut, _) = BuildService(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var resource = await sut.CreateAsync(new CreateResourceDto("Patterns"));

        var act = () => sut.UpdateAsync(new UpdateResourceDto(
            resource.Id, resource.Name, resource.Description, resource.Topic, resource.AreaId, resource.Tags,
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveAsync_ExcludesArchivedResources()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesArchivedResources));
        db.Resources.Add(CreateResource(DefaultUserId, "Active"));
        db.Resources.Add(CreateResource(DefaultUserId, "Archived", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().ContainSingle()
            .Which.Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherUsersResources()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesOtherUsersResources));
        db.Resources.Add(CreateResource(OtherUserId, "Foreign"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WhenResourceBelongsToAnotherUser_ReturnsNull()
    {
        var (sut, db) = BuildService(nameof(GetByIdAsync_WhenResourceBelongsToAnotherUser_ReturnsNull));
        var foreign = CreateResource(OtherUserId);
        db.Resources.Add(foreign);
        await db.SaveChangesAsync();

        var result = await sut.GetByIdAsync(foreign.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_WithLegacyForeignLinkedNote_ExcludesForeignNoteAndCount()
    {
        var (sut, db) = BuildService(nameof(GetDetailAsync_WithLegacyForeignLinkedNote_ExcludesForeignNoteAndCount));
        var resource = CreateResource(DefaultUserId);
        var foreignNote = new Note
        {
            Id = Guid.NewGuid(),
            UserId = OtherUserId,
            Title = "Secret note",
            ResourceId = resource.Id
        };
        db.AddRange(resource, foreignNote);
        await db.SaveChangesAsync();

        var result = await sut.GetDetailAsync(resource.Id);

        result.Should().NotBeNull();
        result!.NoteCount.Should().Be(0);
        result.Notes.Should().BeEmpty();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_MatchesByName_ReturnsMatchingOnly()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_MatchesByName_ReturnsMatchingOnly));
        db.Resources.Add(CreateResource(DefaultUserId, "kubernetes handbook"));
        db.Resources.Add(CreateResource(DefaultUserId, "gardening guide"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("kubernetes", topic: null);

        result.Should().ContainSingle()
            .Which.Name.Should().Be("kubernetes handbook");
    }

    [Fact]
    public async Task SearchAsync_FiltersByExactTopic()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_FiltersByExactTopic));
        db.Resources.Add(CreateResource(DefaultUserId, "A", topic: "devops"));
        db.Resources.Add(CreateResource(DefaultUserId, "B", topic: "design"));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync(searchText: null, topic: "devops");

        result.Should().ContainSingle()
            .Which.Name.Should().Be("A");
    }

    [Fact]
    public async Task SearchAsync_ExcludesArchivedResources()
    {
        var (sut, db) = BuildService(nameof(SearchAsync_ExcludesArchivedResources));
        db.Resources.Add(CreateResource(DefaultUserId, "kubernetes handbook", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.SearchAsync("kubernetes", topic: null);

        result.Should().BeEmpty();
    }

    // ── Archive / Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_SetsArchivedFlagAndTimestamp));
        var resource = CreateResource(DefaultUserId);
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(resource.Id);

        var stored = await db.Resources.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeTrue();
        stored.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ClearsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_ClearsArchivedFlagAndTimestamp));
        var resource = CreateResource(DefaultUserId, isArchived: true);
        resource.ArchivedAtUtc = DateTime.UtcNow;
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        await sut.RestoreAsync(resource.Id);

        var stored = await db.Resources.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeFalse();
        stored.ArchivedAtUtc.Should().BeNull();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenResourceBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenResourceBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateResource(OtherUserId);
        db.Resources.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(foreign.Id, null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var resource = CreateResource(DefaultUserId);
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(resource.Id, [1, 2, 3]);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
