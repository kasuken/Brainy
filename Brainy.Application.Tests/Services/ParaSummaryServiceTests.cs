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
/// Unit tests for <see cref="IParaSummaryService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class ParaSummaryServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IParaSummaryService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IParaSummaryService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Project CreateProject(string userId, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "P", IsArchived = isArchived };

    private static Area CreateArea(string userId, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "A", IsArchived = isArchived };

    private static Resource CreateResource(string userId, bool isArchived = false)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = "R", IsArchived = isArchived };

    [Fact]
    public async Task GetSummaryAsync_WithNoData_ReturnsAllZeros()
    {
        var (sut, _) = BuildService(nameof(GetSummaryAsync_WithNoData_ReturnsAllZeros));

        var result = await sut.GetSummaryAsync();

        result.ActiveProjectCount.Should().Be(0);
        result.ArchivedProjectCount.Should().Be(0);
        result.ActiveAreaCount.Should().Be(0);
        result.ArchivedAreaCount.Should().Be(0);
        result.ActiveResourceCount.Should().Be(0);
        result.ArchivedResourceCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_SplitsProjectCountsByArchivedFlag()
    {
        var (sut, db) = BuildService(nameof(GetSummaryAsync_SplitsProjectCountsByArchivedFlag));
        db.Projects.AddRange(
            CreateProject(DefaultUserId),
            CreateProject(DefaultUserId),
            CreateProject(DefaultUserId, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetSummaryAsync();

        result.ActiveProjectCount.Should().Be(2);
        result.ArchivedProjectCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_SplitsAreaCountsByArchivedFlag()
    {
        var (sut, db) = BuildService(nameof(GetSummaryAsync_SplitsAreaCountsByArchivedFlag));
        db.Areas.AddRange(
            CreateArea(DefaultUserId),
            CreateArea(DefaultUserId, isArchived: true),
            CreateArea(DefaultUserId, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetSummaryAsync();

        result.ActiveAreaCount.Should().Be(1);
        result.ArchivedAreaCount.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_SplitsResourceCountsByArchivedFlag()
    {
        var (sut, db) = BuildService(nameof(GetSummaryAsync_SplitsResourceCountsByArchivedFlag));
        db.Resources.AddRange(
            CreateResource(DefaultUserId),
            CreateResource(DefaultUserId),
            CreateResource(DefaultUserId, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetSummaryAsync();

        result.ActiveResourceCount.Should().Be(2);
        result.ArchivedResourceCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_ExcludesOtherUsersData()
    {
        var (sut, db) = BuildService(nameof(GetSummaryAsync_ExcludesOtherUsersData));
        db.Projects.Add(CreateProject(OtherUserId));
        db.Areas.Add(CreateArea(OtherUserId));
        db.Resources.Add(CreateResource(OtherUserId, isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetSummaryAsync();

        result.Should().BeEquivalentTo(new
        {
            ActiveProjectCount = 0,
            ArchivedProjectCount = 0,
            ActiveAreaCount = 0,
            ArchivedAreaCount = 0,
            ActiveResourceCount = 0,
            ArchivedResourceCount = 0
        });
    }
}
