using Brainy.Application.DTOs.Outputs;
using Brainy.Application.Interfaces.Identity;
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
/// Integration tests for <see cref="IOutputService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class OutputServiceTests
{
    private const string DefaultUserId = "test-user-1";

    private static IOutputService BuildService(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        return services.BuildServiceProvider().GetRequiredService<IOutputService>();
    }

    /// <summary>Seeds a <see cref="Note"/> directly into the named in-memory database.</summary>
    private static async Task<Guid> SeedNoteAsync(string dbName, string userId = DefaultUserId)
    {
        var noteId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var seedCtx = new BrainyDbContext(options);
        seedCtx.Notes.Add(new Note
        {
            Id            = noteId,
            UserId        = userId,
            Title         = "Source Note",
            Content       = "note content",
            CreatedAtUtc  = DateTime.UtcNow,
            UpdatedAtUtc  = DateTime.UtcNow
        });
        await seedCtx.SaveChangesAsync();
        return noteId;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId()
    {
        var sut = BuildService(nameof(CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId));

        var result = await sut.CreateAsync(new CreateOutputDto("My first output", "A description", OutputType.BlogPost));

        result.Id.Should().NotBeEmpty();
        result.Title.Should().Be("My first output");
        result.Description.Should().Be("A description");
        result.Type.Should().Be(OutputType.BlogPost);
    }

    [Fact]
    public async Task CreateAsync_PersistsToDatabase()
    {
        var dbName = nameof(CreateAsync_PersistsToDatabase);
        var sut = BuildService(dbName);

        await sut.CreateAsync(new CreateOutputDto("Persisted output", null, OutputType.Report));

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Outputs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyTitle_ThrowsArgumentException()
    {
        var sut = BuildService(nameof(CreateAsync_WithEmptyTitle_ThrowsArgumentException));

        var act = () => sut.CreateAsync(new CreateOutputDto("   ", null, OutputType.BlogPost));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_DefaultsStatusToDraft()
    {
        var sut = BuildService(nameof(CreateAsync_DefaultsStatusToDraft));

        var result = await sut.CreateAsync(new CreateOutputDto("Draft output", null, OutputType.LinkedInPost));

        result.Status.Should().Be(OutputStatus.Draft);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsDto()
    {
        var sut = BuildService(nameof(GetByIdAsync_ExistingId_ReturnsDto));
        var created = await sut.CreateAsync(new CreateOutputDto("Find me", null, OutputType.Report));

        var result = await sut.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Find me");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        var sut = BuildService(nameof(GetByIdAsync_NonExistentId_ReturnsNull));

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_OtherUserOutput_ReturnsNull()
    {
        var dbName = nameof(GetByIdAsync_OtherUserOutput_ReturnsNull);
        var userA  = BuildService(dbName, "user-a");
        var userB  = BuildService(dbName, "user-b");

        var createdByA = await userA.CreateAsync(new CreateOutputDto("Private output", null, OutputType.BlogPost));

        var result = await userB.GetByIdAsync(createdByA.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesArchived()
    {
        var sut = BuildService(nameof(GetAllActiveAsync_ExcludesArchived));
        var active   = await sut.CreateAsync(new CreateOutputDto("Active output",   null, OutputType.BlogPost));
        var toArchive = await sut.CreateAsync(new CreateOutputDto("Archived output", null, OutputType.BlogPost));
        await sut.ArchiveAsync(toArchive.Id);

        var result = await sut.GetAllActiveAsync();

        result.Should().ContainSingle()
              .Which.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task GetAllArchivedAsync_ReturnsOnlyArchived()
    {
        var sut = BuildService(nameof(GetAllArchivedAsync_ReturnsOnlyArchived));
        await sut.CreateAsync(new CreateOutputDto("Active output", null, OutputType.BlogPost));
        var toArchive = await sut.CreateAsync(new CreateOutputDto("Archived output", null, OutputType.BlogPost));
        await sut.ArchiveAsync(toArchive.Id);

        var result = await sut.GetAllArchivedAsync();

        result.Should().ContainSingle()
              .Which.Id.Should().Be(toArchive.Id);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProjectAsync_ReturnsOutputsForProject()
    {
        var sut       = BuildService(nameof(GetByProjectAsync_ReturnsOutputsForProject));
        var projectId = Guid.NewGuid();

        var linked    = await sut.CreateAsync(new CreateOutputDto("Linked",   null, OutputType.Report, ProjectId: projectId));
        var unlinked  = await sut.CreateAsync(new CreateOutputDto("Unlinked", null, OutputType.Report));

        var result = await sut.GetByProjectAsync(projectId);

        result.Should().ContainSingle().Which.Id.Should().Be(linked.Id);
    }

    [Fact]
    public async Task GetByGoalAsync_ReturnsOutputsForGoal()
    {
        var sut    = BuildService(nameof(GetByGoalAsync_ReturnsOutputsForGoal));
        var goalId = Guid.NewGuid();

        var linked   = await sut.CreateAsync(new CreateOutputDto("Goal output", null, OutputType.Report, GoalId: goalId));
        var unlinked = await sut.CreateAsync(new CreateOutputDto("Other output", null, OutputType.Report));

        var result = await sut.GetByGoalAsync(goalId);

        result.Should().ContainSingle().Which.Id.Should().Be(linked.Id);
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetailAsync_ExistingId_ReturnsDetailWithSourceNotes()
    {
        var dbName = nameof(GetDetailAsync_ExistingId_ReturnsDetailWithSourceNotes);
        var sut    = BuildService(dbName);
        var noteId = await SeedNoteAsync(dbName);

        var created = await sut.CreateAsync(new CreateOutputDto("Output with source", null, OutputType.BlogPost));
        await sut.AddSourceNoteAsync(created.Id, noteId);

        var detail = await sut.GetDetailAsync(created.Id);

        detail.Should().NotBeNull();
        detail!.Id.Should().Be(created.Id);
        detail.SourceNotes.Should().ContainSingle()
              .Which.NoteId.Should().Be(noteId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesFields()
    {
        var sut     = BuildService(nameof(UpdateAsync_ChangesFields));
        var created = await sut.CreateAsync(new CreateOutputDto("Original title", "Original desc", OutputType.BlogPost));

        var updated = await sut.UpdateAsync(new UpdateOutputDto(
            created.Id,
            "Updated title",
            "Updated desc",
            OutputType.Report,
            OutputStatus.InReview,
            "Updated content",
            null, null, null));

        updated.Title.Should().Be("Updated title");
        updated.Description.Should().Be("Updated desc");
        updated.Type.Should().Be(OutputType.Report);
        updated.Status.Should().Be(OutputStatus.InReview);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsIsArchivedAndDate()
    {
        var dbName  = nameof(ArchiveAsync_SetsIsArchivedAndDate);
        var sut     = BuildService(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("To archive", null, OutputType.BlogPost));

        await sut.ArchiveAsync(created.Id);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var ctx = new BrainyDbContext(options);
        var stored = await ctx.Outputs.SingleAsync(o => o.Id == created.Id);

        stored.IsArchived.Should().BeTrue();
        stored.ArchivedDate.Should().NotBeNull();
        stored.Status.Should().Be(OutputStatus.Archived);
    }

    [Fact]
    public async Task RestoreAsync_ClearsIsArchivedAndDate()
    {
        var dbName  = nameof(RestoreAsync_ClearsIsArchivedAndDate);
        var sut     = BuildService(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("Archived output", null, OutputType.BlogPost));
        await sut.ArchiveAsync(created.Id);

        await sut.RestoreAsync(created.Id);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var ctx = new BrainyDbContext(options);
        var stored = await ctx.Outputs.SingleAsync(o => o.Id == created.Id);

        stored.IsArchived.Should().BeFalse();
        stored.ArchivedDate.Should().BeNull();
        stored.Status.Should().Be(OutputStatus.Draft);
    }

    [Fact]
    public async Task PublishAsync_SetsStatusPublishedAndPublishedDate()
    {
        var dbName  = nameof(PublishAsync_SetsStatusPublishedAndPublishedDate);
        var sut     = BuildService(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("Ready to publish", null, OutputType.BlogPost));

        await sut.PublishAsync(created.Id);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var ctx = new BrainyDbContext(options);
        var stored = await ctx.Outputs.SingleAsync(o => o.Id == created.Id);

        stored.Status.Should().Be(OutputStatus.Published);
        stored.PublishedDate.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromDatabase()
    {
        var sut     = BuildService(nameof(DeleteAsync_RemovesFromDatabase));
        var created = await sut.CreateAsync(new CreateOutputDto("To delete", null, OutputType.BlogPost));

        await sut.DeleteAsync(created.Id);

        var result = await sut.GetByIdAsync(created.Id);
        result.Should().BeNull();
    }

    // ── Source Notes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSourceNoteAsync_LinksNoteToOutput()
    {
        var dbName  = nameof(AddSourceNoteAsync_LinksNoteToOutput);
        var sut     = BuildService(dbName);
        var noteId  = await SeedNoteAsync(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("Output", null, OutputType.BlogPost));

        await sut.AddSourceNoteAsync(created.Id, noteId);

        var detail = await sut.GetDetailAsync(created.Id);
        detail!.SourceNotes.Should().ContainSingle()
               .Which.NoteId.Should().Be(noteId);
    }

    [Fact]
    public async Task RemoveSourceNoteAsync_UnlinksNote()
    {
        var dbName  = nameof(RemoveSourceNoteAsync_UnlinksNote);
        var sut     = BuildService(dbName);
        var noteId  = await SeedNoteAsync(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("Output", null, OutputType.BlogPost));
        await sut.AddSourceNoteAsync(created.Id, noteId);

        await sut.RemoveSourceNoteAsync(created.Id, noteId);

        var detail = await sut.GetDetailAsync(created.Id);
        detail!.SourceNotes.Should().BeEmpty();
    }

    [Fact]
    public async Task AddSourceNoteAsync_IsIdempotent()
    {
        var dbName  = nameof(AddSourceNoteAsync_IsIdempotent);
        var sut     = BuildService(dbName);
        var noteId  = await SeedNoteAsync(dbName);
        var created = await sut.CreateAsync(new CreateOutputDto("Output", null, OutputType.BlogPost));

        // Adding the same note twice must not throw and must not duplicate the link.
        await sut.AddSourceNoteAsync(created.Id, noteId);
        var act = () => sut.AddSourceNoteAsync(created.Id, noteId);

        await act.Should().NotThrowAsync();

        var detail = await sut.GetDetailAsync(created.Id);
        detail!.SourceNotes.Should().ContainSingle();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_MatchesTitle()
    {
        var sut = BuildService(nameof(SearchAsync_MatchesTitle));
        var hit  = await sut.CreateAsync(new CreateOutputDto("Unique keyword here", null, OutputType.BlogPost));
        await sut.CreateAsync(new CreateOutputDto("Completely different", null, OutputType.BlogPost));

        var result = await sut.SearchAsync("Unique keyword");

        result.Should().ContainSingle().Which.Id.Should().Be(hit.Id);
    }

    [Fact]
    public async Task SearchAsync_MatchesContent()
    {
        var sut = BuildService(nameof(SearchAsync_MatchesContent));
        var hit  = await sut.CreateAsync(new CreateOutputDto("Some output", null, OutputType.BlogPost, Content: "secretterm inside content"));
        await sut.CreateAsync(new CreateOutputDto("Another output", null, OutputType.BlogPost, Content: "nothing special"));

        var result = await sut.SearchAsync("secretterm");

        result.Should().ContainSingle().Which.Id.Should().Be(hit.Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesArchived()
    {
        var sut      = BuildService(nameof(SearchAsync_ExcludesArchived));
        var archived = await sut.CreateAsync(new CreateOutputDto("findme archived", null, OutputType.BlogPost));
        await sut.ArchiveAsync(archived.Id);

        var result = await sut.SearchAsync("findme");

        result.Should().BeEmpty();
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_ReturnsCounts()
    {
        var sut = BuildService(nameof(GetMetricsAsync_ReturnsCounts));

        var draft     = await sut.CreateAsync(new CreateOutputDto("Draft",     null, OutputType.BlogPost));
        var toPublish = await sut.CreateAsync(new CreateOutputDto("Published", null, OutputType.Report));
        var toArchive = await sut.CreateAsync(new CreateOutputDto("Archived",  null, OutputType.LinkedInPost));

        await sut.PublishAsync(toPublish.Id);
        await sut.ArchiveAsync(toArchive.Id);

        var metrics = await sut.GetMetricsAsync();

        metrics.TotalOutputs.Should().Be(3);
        metrics.DraftCount.Should().Be(1);
        metrics.PublishedCount.Should().Be(1);
        metrics.ArchivedCount.Should().Be(1);
    }
}
