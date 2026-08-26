using Brainy.Application.Common;
using Brainy.Application.DTOs.Notes;
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
/// Unit tests for <see cref="INoteService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class NoteServiceTests
{
    private const string DefaultUserId = "test-user-1";
    private const string OtherUserId = "test-user-2";

    private static INoteService BuildService(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        return services.BuildServiceProvider().GetRequiredService<INoteService>();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId()
    {
        var sut = BuildService(nameof(CreateAsync_WithValidDto_ReturnsDtoWithGeneratedId));

        var result = await sut.CreateAsync(new CreateNoteDto("My first note", "Some content", ParaCategory.Area));

        result.Id.Should().NotBeEmpty();
        result.Title.Should().Be("My first note");
        result.Content.Should().Be("Some content");
        result.ParaCategory.Should().Be(ParaCategory.Area);
        result.Status.Should().Be(NoteStatus.Inbox);
        result.AiSummary.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_PersistsNoteToDatabase()
    {
        var dbName = nameof(CreateAsync_PersistsNoteToDatabase);
        var sut = BuildService(dbName);

        await sut.CreateAsync(new CreateNoteDto("Persisted note"));

        // Verify directly via a fresh context on the same named DB.
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        (await context.Notes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var sut = BuildService(nameof(CreateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Area")]
    [InlineData("Resource")]
    public async Task CreateAsync_WithForeignRelatedEntity_ThrowsKeyNotFoundException(string entityType)
    {
        var dbName = $"{nameof(CreateAsync_WithForeignRelatedEntity_ThrowsKeyNotFoundException)}-{entityType}";
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var db = new BrainyDbContext(options);
        var foreignId = Guid.NewGuid();

        switch (entityType)
        {
            case "Project":
                db.Projects.Add(new Project { Id = foreignId, UserId = OtherUserId, Name = "Foreign" });
                break;
            case "Area":
                db.Areas.Add(new Area { Id = foreignId, UserId = OtherUserId, Name = "Foreign" });
                break;
            case "Resource":
                db.Resources.Add(new Resource { Id = foreignId, UserId = OtherUserId, Name = "Foreign" });
                break;
        }
        await db.SaveChangesAsync();

        var sut = BuildService(dbName);
        var dto = entityType switch
        {
            "Project" => new CreateNoteDto("Private", ProjectId: foreignId),
            "Area" => new CreateNoteDto("Private", AreaId: foreignId),
            _ => new CreateNoteDto("Private", ResourceId: foreignId)
        };

        var act = () => sut.CreateAsync(dto);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Notes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithOwnedRelatedEntities_PersistsAllLinks()
    {
        var dbName = nameof(CreateAsync_WithOwnedRelatedEntities_PersistsAllLinks);
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var db = new BrainyDbContext(options);
        var project = new Project { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Project" };
        var area = new Area { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Area" };
        var resource = new Resource { Id = Guid.NewGuid(), UserId = DefaultUserId, Name = "Resource" };
        db.AddRange(project, area, resource);
        await db.SaveChangesAsync();

        var sut = BuildService(dbName);
        var result = await sut.CreateAsync(new CreateNoteDto(
            "Linked", ProjectId: project.Id, AreaId: area.Id, ResourceId: resource.Id));

        result.ProjectId.Should().Be(project.Id);
        result.AreaId.Should().Be(area.Id);
        result.ResourceId.Should().Be(resource.Id);
    }

    [Fact]
    public async Task CreateAsync_WithTags_ReturnsAndPersistsNormalizedDistinctTags()
    {
        var dbName = nameof(CreateAsync_WithTags_ReturnsAndPersistsNormalizedDistinctTags);
        var sut = BuildService(dbName);

        var created = await sut.CreateAsync(new CreateNoteDto(
            "Tagged note",
            Tags: [" research ", "writing", "Research", "writing"]));

        created.Tags.Should().Equal("research", "writing");

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        var stored = await context.Notes
            .Include(note => note.Tags)
            .SingleAsync(note => note.Id == created.Id);

        stored.Tags.Select(tag => tag.Name)
            .Should().BeEquivalentTo(["research", "writing"]);
    }

    // ── Read (single) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WhenNoteExists_ReturnsCorrectDto()
    {
        var sut = BuildService(nameof(GetByIdAsync_WhenNoteExists_ReturnsCorrectDto));
        var created = await sut.CreateAsync(new CreateNoteDto("Find me", "body", ParaCategory.Resource));

        var result = await sut.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Find me");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNoteDoesNotExist_ReturnsNull()
    {
        var sut = BuildService(nameof(GetByIdAsync_WhenNoteDoesNotExist_ReturnsNull));

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── Read (list) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_WhenNoNotes_ReturnsEmptyList()
    {
        var sut = BuildService(nameof(GetAllAsync_WhenNoNotes_ReturnsEmptyList));

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllNotes()
    {
        var sut = BuildService(nameof(GetAllAsync_ReturnsAllNotes));
        await sut.CreateAsync(new CreateNoteDto("Note A"));
        await sut.CreateAsync(new CreateNoteDto("Note B"));
        await sut.CreateAsync(new CreateNoteDto("Note C"));

        var result = await sut.GetAllAsync();

        result.Should().HaveCount(3);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithValidDto_UpdatesAllFields()
    {
        var sut = BuildService(nameof(UpdateAsync_WithValidDto_UpdatesAllFields));
        var created = await sut.CreateAsync(new CreateNoteDto("Original"));

        var updated = await sut.UpdateAsync(new UpdateNoteDto(
            created.Id, "Updated title", "Updated content", "AI summary text",
            NoteStatus.Distilled, ParaCategory.Archive,
            null, null, null));

        updated.Title.Should().Be("Updated title");
        updated.Content.Should().Be("Updated content");
        updated.AiSummary.Should().Be("AI summary text");
        updated.Status.Should().Be(NoteStatus.Distilled);
        updated.ParaCategory.Should().Be(ParaCategory.Archive);
    }

    [Fact]
    public async Task UpdateAsync_WithTagsOnly_ReplacesPersistedTagsAndRefreshesUpdatedTimestamp()
    {
        var dbName = nameof(UpdateAsync_WithTagsOnly_ReplacesPersistedTagsAndRefreshesUpdatedTimestamp);
        var sut = BuildService(dbName);
        var created = await sut.CreateAsync(new CreateNoteDto(
            "Original",
            Tags: ["alpha", "beta"]));

        await Task.Delay(20);

        var updated = await sut.UpdateAsync(new UpdateNoteDto(
            created.Id,
            created.Title,
            created.Content,
            created.AiSummary,
            created.Status,
            created.ParaCategory,
            created.ProjectId,
            created.AreaId,
            created.ResourceId,
            Tags: ["beta", "gamma"]));

        updated.Tags.Should().Equal("beta", "gamma");
        updated.UpdatedAtUtc.Should().BeAfter(created.UpdatedAtUtc);

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        var stored = await context.Notes
            .Include(note => note.Tags)
            .SingleAsync(note => note.Id == created.Id);

        stored.Tags.Select(tag => tag.Name)
            .Should().BeEquivalentTo(["beta", "gamma"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = BuildService(nameof(UpdateAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException));

        var act = () => sut.UpdateAsync(new UpdateNoteDto(
            Guid.NewGuid(), "Title", "Content", null,
            NoteStatus.Inbox, ParaCategory.Project,
            null, null, null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithNullDto_ThrowsArgumentNullException()
    {
        var sut = BuildService(nameof(UpdateAsync_WithNullDto_ThrowsArgumentNullException));

        var act = () => sut.UpdateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Process")]
    [InlineData("BulkProcess")]
    [InlineData("LinkToProject")]
    public async Task Mutations_WithForeignRelatedEntity_ThrowKeyNotFoundException(string operation)
    {
        var dbName = $"{nameof(Mutations_WithForeignRelatedEntity_ThrowKeyNotFoundException)}-{operation}";
        var sut = BuildService(dbName);
        var note = await sut.CreateAsync(new CreateNoteDto("Owned note"));
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var db = new BrainyDbContext(options);
        var foreignProject = new Project { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Foreign project" };
        var foreignArea = new Area { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Foreign area" };
        var foreignResource = new Resource { Id = Guid.NewGuid(), UserId = OtherUserId, Name = "Foreign resource" };
        db.AddRange(foreignProject, foreignArea, foreignResource);
        await db.SaveChangesAsync();

        Func<Task> act = operation switch
        {
            "Update" => async () => { await sut.UpdateAsync(new UpdateNoteDto(
                note.Id, note.Title, note.Content, null, note.Status, note.ParaCategory,
                null, foreignArea.Id, null)); },
            "Process" => async () => { await sut.ProcessNoteAsync(new ProcessNoteDto(
                note.Id, ParaCategory.Resource, ResourceId: foreignResource.Id)); },
            "BulkProcess" => async () => { await sut.BulkProcessInboxAsync(
                [note.Id], ParaCategory.Project, NoteStatus.Active, projectId: foreignProject.Id); },
            _ => async () => { await sut.LinkToProjectAsync(note.Id, foreignProject.Id); }
        };

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenNoteExists_RemovesFromDatabase()
    {
        var sut = BuildService(nameof(DeleteAsync_WhenNoteExists_RemovesFromDatabase));
        var created = await sut.CreateAsync(new CreateNoteDto("To delete"));

        await sut.DeleteAsync(created.Id);

        var result = await sut.GetByIdAsync(created.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = BuildService(nameof(DeleteAsync_WhenNoteDoesNotExist_ThrowsKeyNotFoundException));

        var act = () => sut.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var sut = BuildService(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var created = await sut.CreateAsync(new CreateNoteDto("Original"));

        // The InMemory provider validates concurrency tokens but does not regenerate
        // them on update, so a mismatched token simulates the stale value a second
        // tab would hold after SQL Server bumps the rowversion.
        var act = () => sut.UpdateAsync(new UpdateNoteDto(
            created.Id, "My stale edit", "", null,
            created.Status, created.ParaCategory, null, null, null,
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task UpdateAsync_WithCurrentRowVersion_Succeeds()
    {
        var dbName = nameof(UpdateAsync_WithCurrentRowVersion_Succeeds);
        var rowVersion = new byte[] { 4, 5, 6 };
        var noteId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;

        await using (var seedContext = new BrainyDbContext(options))
        {
            seedContext.Notes.Add(new Note
            {
                Id = noteId,
                UserId = DefaultUserId,
                Title = "Original",
                Content = "Before",
                Status = NoteStatus.Inbox,
                ParaCategory = ParaCategory.Project,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                RowVersion = rowVersion
            });

            await seedContext.SaveChangesAsync();
        }

        var sut = BuildService(dbName);

        var updated = await sut.UpdateAsync(new UpdateNoteDto(
            noteId,
            "Updated title",
            "Updated content",
            null,
            NoteStatus.Distilled,
            ParaCategory.Archive,
            null,
            null,
            null,
            RowVersion: rowVersion));

        updated.Title.Should().Be("Updated title");
        updated.Content.Should().Be("Updated content");
        updated.Status.Should().Be(NoteStatus.Distilled);
        updated.ParaCategory.Should().Be(ParaCategory.Archive);
        updated.RowVersion.Should().Equal(rowVersion);
    }

    [Fact]
    public async Task DeleteAsync_WhenNoteIsRelationshipTarget_RemovesRelationshipLinks()
    {
        var dbName = nameof(DeleteAsync_WhenNoteIsRelationshipTarget_RemovesRelationshipLinks);
        var sut = BuildService(dbName);
        var source = await sut.CreateAsync(new CreateNoteDto("Source note"));
        var target = await sut.CreateAsync(new CreateNoteDto("Target note"));

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new BrainyDbContext(options))
        {
            seedContext.NoteRelationships.Add(new NoteRelationship
            {
                Id = Guid.NewGuid(),
                SourceNoteId = source.Id,
                TargetNoteId = target.Id,
                Type = RelationshipType.Related
            });
            await seedContext.SaveChangesAsync();
        }

        await sut.DeleteAsync(target.Id);

        await using var verifyContext = new BrainyDbContext(options);
        (await verifyContext.NoteRelationships.CountAsync()).Should().Be(0);
        (await verifyContext.Notes.CountAsync(n => n.Id == source.Id)).Should().Be(1);
    }

    // ── Per-user isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_OnlyReturnsNotesOwnedByCurrentUser()
    {
        var dbName = nameof(GetAllAsync_OnlyReturnsNotesOwnedByCurrentUser);
        var userA = BuildService(dbName, "user-a");
        var userB = BuildService(dbName, "user-b");

        await userA.CreateAsync(new CreateNoteDto("A note"));
        await userB.CreateAsync(new CreateNoteDto("B note 1"));
        await userB.CreateAsync(new CreateNoteDto("B note 2"));

        var resultForA = await userA.GetAllAsync();

        resultForA.Should().ContainSingle()
            .Which.Title.Should().Be("A note");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNoteBelongsToAnotherUser_ReturnsNull()
    {
        var dbName = nameof(GetByIdAsync_WhenNoteBelongsToAnotherUser_ReturnsNull);
        var userA = BuildService(dbName, "user-a");
        var userB = BuildService(dbName, "user-b");
        var createdByA = await userA.CreateAsync(new CreateNoteDto("A private note"));

        var result = await userB.GetByIdAsync(createdByA.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var dbName = nameof(DeleteAsync_WhenNoteBelongsToAnotherUser_ThrowsKeyNotFoundException);
        var userA = BuildService(dbName, "user-a");
        var userB = BuildService(dbName, "user-b");
        var createdByA = await userA.CreateAsync(new CreateNoteDto("A private note"));

        var act = () => userB.DeleteAsync(createdByA.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_StampsNoteWithCurrentUserId()
    {
        var dbName = nameof(CreateAsync_StampsNoteWithCurrentUserId);
        var sut = BuildService(dbName, "owner-123");

        var created = await sut.CreateAsync(new CreateNoteDto("Owned note"));

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var context = new BrainyDbContext(options);
        var stored = await context.Notes.SingleAsync(n => n.Id == created.Id);
        stored.UserId.Should().Be("owner-123");
    }

    [Fact]
    public async Task ProcessNoteAsync_WhenArchived_SetsArchiveFlags()
    {
        var sut = BuildService(nameof(ProcessNoteAsync_WhenArchived_SetsArchiveFlags));
        var created = await sut.CreateAsync(new CreateNoteDto("Inbox item"));

        var updated = await sut.ProcessNoteAsync(new ProcessNoteDto(
            created.Id,
            ParaCategory.Archive,
            NoteStatus.Archived));

        updated.Status.Should().Be(NoteStatus.Archived);
        updated.IsArchived.Should().BeTrue();
        updated.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessNoteAsync_WithReviewedContent_UpdatesNoteAndProcessesItTogether()
    {
        var sut = BuildService(nameof(ProcessNoteAsync_WithReviewedContent_UpdatesNoteAndProcessesItTogether));
        var created = await sut.CreateAsync(new CreateNoteDto("Rough title", "Rough content"));

        var updated = await sut.ProcessNoteAsync(new ProcessNoteDto(
            created.Id,
            ParaCategory.Resource,
            NoteStatus.Distilled,
            Title: "Reviewed title",
            Content: "Reviewed content",
            RowVersion: created.RowVersion));

        updated.Title.Should().Be("Reviewed title");
        updated.Content.Should().Be("Reviewed content");
        updated.Status.Should().Be(NoteStatus.Distilled);
        updated.ParaCategory.Should().Be(ParaCategory.Resource);
        updated.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessNoteAsync_WithBlankReviewedTitle_ThrowsArgumentException()
    {
        var sut = BuildService(nameof(ProcessNoteAsync_WithBlankReviewedTitle_ThrowsArgumentException));
        var created = await sut.CreateAsync(new CreateNoteDto("Inbox item"));

        var act = () => sut.ProcessNoteAsync(new ProcessNoteDto(
            created.Id,
            ParaCategory.Project,
            Title: " "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProcessNoteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var sut = BuildService(nameof(ProcessNoteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var created = await sut.CreateAsync(new CreateNoteDto("Inbox item"));

        var act = () => sut.ProcessNoteAsync(new ProcessNoteDto(
            created.Id,
            ParaCategory.Project,
            Title: "Reviewed title",
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task GetAllArchivedAsync_IncludesLegacyStatusArchivedNotes()
    {
        var dbName = nameof(GetAllArchivedAsync_IncludesLegacyStatusArchivedNotes);
        var sut = BuildService(dbName);
        var created = await sut.CreateAsync(new CreateNoteDto("Legacy archived note"));

        var options = new DbContextOptionsBuilder<BrainyDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var context = new BrainyDbContext(options))
        {
            var stored = await context.Notes.SingleAsync(n => n.Id == created.Id);
            stored.Status = NoteStatus.Archived;
            stored.IsArchived = false;
            stored.ArchivedAtUtc = null;
            await context.SaveChangesAsync();
        }

        var archived = await sut.GetAllArchivedAsync();
        archived.Select(n => n.Id).Should().Contain(created.Id);
    }
}
