using Brainy.Application.DTOs.Resources;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using FluentAssertions;
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

        var act = () => sut.DeleteAsync(foreign.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
