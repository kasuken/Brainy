using Brainy.Application.Common;
using Brainy.Application.DTOs.Ideas;
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
/// Unit tests for <see cref="IIdeaService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Focuses on capture, archive lifecycle, and the commit-to-project flow.
/// </summary>
public class IdeaServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IIdeaService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IIdeaService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Idea CreateIdea(
        string userId,
        string title = "Idea",
        IdeaStatus status = IdeaStatus.Captured,
        bool isArchived = false,
        bool withCommitCriteria = false)
    {
        var idea = new Idea
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Status = status,
            IsArchived = isArchived
        };

        if (withCommitCriteria)
        {
            idea.TargetUserAndProblem = "Solo founders who lose track of newsletter ideas";
            idea.SuitabilityReason = "I've built two newsletters before";
            idea.Evidence = "Three people asked me for this in the last week";
            idea.ValidationExperiment = "Ship a landing page and collect signups for a week";
            idea.ReplacedCommitment = "Pausing the podcast side project";
        }

        return idea;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_PersistsCapturedIdeaForCurrentUser()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithValidDto_PersistsCapturedIdeaForCurrentUser));

        var result = await sut.CreateAsync(new CreateIdeaDto("  Newsletter tool  ", "desc", null));

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.UserId.Should().Be(DefaultUserId);
        stored.Title.Should().Be("Newsletter tool");
        stored.Status.Should().Be(IdeaStatus.Captured);
    }

    [Fact]
    public async Task CreateAsync_WithBlankTitle_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(CreateAsync_WithBlankTitle_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateIdeaDto("   ", null, null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WithForeignArea_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithForeignArea_ThrowsKeyNotFoundException));
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        db.Areas.Add(foreignArea);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateIdeaDto("Idea", null, foreignArea.Id));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Ideas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithOwnedArea_ReturnsAreaName()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithOwnedArea_ReturnsAreaName));
        var area = new Area { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Owned area" };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateIdeaDto("Idea", null, area.Id));

        result.AreaId.Should().Be(area.Id);
        result.AreaName.Should().Be(area.Name);
    }

    [Fact]
    public async Task UpdateAsync_WithForeignArea_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(UpdateAsync_WithForeignArea_ThrowsKeyNotFoundException));
        var idea = CreateIdea(DefaultUserId);
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        db.AddRange(idea, foreignArea);
        await db.SaveChangesAsync();

        var act = () => sut.UpdateAsync(new UpdateIdeaDto(
            idea.Id, idea.Title, null, foreignArea.Id, IdeaPriority.Medium,
            IdeaStatus.Captured, null, null, null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (sut, _) = BuildService(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var idea = await sut.CreateAsync(new CreateIdeaDto(
            "Build it", null, null, IdeaPriority.Medium));

        var act = () => sut.UpdateAsync(new UpdateIdeaDto(
            idea.Id, idea.Title, idea.Description, idea.AreaId, idea.Priority, idea.Status,
            null, null, null,
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllActiveAsync_ExcludesArchivedIdeas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesArchivedIdeas));
        db.Ideas.Add(CreateIdea(DefaultUserId, "Active"));
        db.Ideas.Add(CreateIdea(DefaultUserId, "Archived", isArchived: true));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().ContainSingle()
            .Which.Title.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesOtherUsersIdeas()
    {
        var (sut, db) = BuildService(nameof(GetAllActiveAsync_ExcludesOtherUsersIdeas));
        db.Ideas.Add(CreateIdea(OtherUserId, "Foreign"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllActiveAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WithLegacyForeignArea_DoesNotLeakAreaName()
    {
        var (sut, db) = BuildService(nameof(GetByIdAsync_WithLegacyForeignArea_DoesNotLeakAreaName));
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        var idea = CreateIdea(DefaultUserId);
        idea.AreaId = foreignArea.Id;
        idea.Area = foreignArea;
        db.AddRange(foreignArea, idea);
        await db.SaveChangesAsync();

        var result = await sut.GetByIdAsync(idea.Id);

        result.Should().NotBeNull();
        result!.AreaName.Should().BeNull();
    }

    [Fact]
    public async Task GetByAreaAsync_WithForeignArea_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(GetByAreaAsync_WithForeignArea_ThrowsKeyNotFoundException));
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Secret area" };
        db.Areas.Add(foreignArea);
        await db.SaveChangesAsync();

        var act = () => sut.GetByAreaAsync(foreignArea.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Archive / Restore ─────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(ArchiveAsync_SetsArchivedFlagAndTimestamp));
        var idea = CreateIdea(DefaultUserId);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.ArchiveAsync(idea.Id);

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeTrue();
        stored.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ClearsArchivedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(RestoreAsync_ClearsArchivedFlagAndTimestamp));
        var idea = CreateIdea(DefaultUserId, isArchived: true);
        idea.ArchivedAtUtc = DateTime.UtcNow;
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.RestoreAsync(idea.Id);

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.IsArchived.Should().BeFalse();
        stored.ArchivedAtUtc.Should().BeNull();
    }

    // ── Commit to project ─────────────────────────────────────────────────────

    [Fact]
    public async Task CommitToProjectAsync_WithAllCriteriaFilled_CreatesProjectAndMarksIdeaCommitted()
    {
        var (sut, db) = BuildService(nameof(CommitToProjectAsync_WithAllCriteriaFilled_CreatesProjectAndMarksIdeaCommitted));
        var idea = CreateIdea(DefaultUserId, "Launch newsletter", withCommitCriteria: true);
        idea.Description = "A weekly roundup";
        idea.Research = "Some research";
        idea.Competitors = "Some competitors";
        idea.Notes = "Some notes";
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        await sut.CommitToProjectAsync(idea.Id);

        var project = await db.Projects.AsNoTracking().SingleAsync();
        project.Name.Should().Be("Launch newsletter");
        project.UserId.Should().Be(DefaultUserId);
        project.Status.Should().Be(ProjectStatus.NotStarted);

        var stored = await db.Ideas.AsNoTracking().SingleAsync();
        stored.Status.Should().Be(IdeaStatus.Committed);
        stored.CommittedProjectId.Should().Be(project.Id);
        stored.CommittedAtUtc.Should().NotBeNull();

        // Only a link and the decision record remain; bulky content is cleared.
        stored.Description.Should().BeNull();
        stored.Research.Should().BeNull();
        stored.Competitors.Should().BeNull();
        stored.Notes.Should().BeNull();
        stored.TargetUserAndProblem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CommitToProjectAsync_WhenAlreadyCommitted_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(CommitToProjectAsync_WhenAlreadyCommitted_ThrowsInvalidOperationException));
        var idea = CreateIdea(DefaultUserId, status: IdeaStatus.Committed, withCommitCriteria: true);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var act = () => sut.CommitToProjectAsync(idea.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CommitToProjectAsync_WhenCriteriaMissing_ThrowsInvalidOperationException()
    {
        var (sut, db) = BuildService(nameof(CommitToProjectAsync_WhenCriteriaMissing_ThrowsInvalidOperationException));
        var idea = CreateIdea(DefaultUserId); // No commitment criteria filled.
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var act = () => sut.CommitToProjectAsync(idea.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Projects.CountAsync()).Should().Be(0);
    }


    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenIdeaBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenIdeaBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateIdea(OtherUserId);
        db.Ideas.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(foreign.Id, null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var idea = CreateIdea(DefaultUserId);
        db.Ideas.Add(idea);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(idea.Id, [1, 2, 3]);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
