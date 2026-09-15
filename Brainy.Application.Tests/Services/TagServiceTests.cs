using Brainy.Application.DTOs.Notes;
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
/// Unit tests for <see cref="ITagService"/> resolved via the real DI container with an
/// EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class TagServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (ITagService tags, INoteService notes, IResourceService resources, BrainyDbContext db) BuildServices(
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
        return (
            sp.GetRequiredService<ITagService>(),
            sp.GetRequiredService<INoteService>(),
            sp.GetRequiredService<IResourceService>(),
            sp.GetRequiredService<BrainyDbContext>());
    }

    private static Tag CreateTag(string userId, string name)
        => new() { Id = Guid.NewGuid(), UserId = userId, Name = name };

    private static Note CreateNote(string userId, bool isArchived = false, params Tag[] tags)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "N",
            Content = "c",
            IsArchived = isArchived,
            Tags = tags.ToList()
        };

    private static Resource CreateResource(string userId, bool isArchived = false, params Tag[] tags)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "R",
            IsArchived = isArchived,
            Tags = tags.ToList()
        };

    // ── GetAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyCurrentUsersTags()
    {
        var (sut, _, _, db) = BuildServices(nameof(GetAllAsync_ReturnsOnlyCurrentUsersTags));
        db.Tags.Add(CreateTag(DefaultUserId, "mine"));
        db.Tags.Add(CreateTag(OtherUserId, "theirs"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("mine");
    }

    [Fact]
    public async Task GetAllAsync_CountsOnlyActiveNotesAndResources()
    {
        var (sut, _, _, db) = BuildServices(nameof(GetAllAsync_CountsOnlyActiveNotesAndResources));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: false, tag));
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: true, tag));
        db.Resources.Add(CreateResource(DefaultUserId, isArchived: false, tag));
        await db.SaveChangesAsync();

        var result = await sut.GetAllAsync();

        var dto = result.Should().ContainSingle().Which;
        dto.NoteCount.Should().Be(1);
        dto.ResourceCount.Should().Be(1);
        dto.UsageCount.Should().Be(2);
        dto.IsUnused.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllAsync_TagWithNoActiveUsage_IsUnused()
    {
        var (sut, _, _, db) = BuildServices(nameof(GetAllAsync_TagWithNoActiveUsage_IsUnused));
        db.Tags.Add(CreateTag(DefaultUserId, "orphan"));
        await db.SaveChangesAsync();

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle().Which.IsUnused.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllAsync_LastUsedAtUtc_IsMostRecentNoteOrResourceUpdate()
    {
        var (sut, _, _, db) = BuildServices(nameof(GetAllAsync_LastUsedAtUtc_IsMostRecentNoteOrResourceUpdate));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        var note = CreateNote(DefaultUserId, isArchived: false, tag);
        var resource = CreateResource(DefaultUserId, isArchived: false, tag);
        db.Notes.Add(note);
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        note.UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        resource.UpdatedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle().Which.LastUsedAtUtc.Should().Be(resource.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetAllAsync_AfterNoteArchived_RefreshesUsageCount()
    {
        var (sut, notes, _, db) = BuildServices(nameof(GetAllAsync_AfterNoteArchived_RefreshesUsageCount));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        var note = CreateNote(DefaultUserId, isArchived: false, tag);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        var before = await sut.GetAllAsync();
        before.Should().ContainSingle().Which.NoteCount.Should().Be(1);

        await notes.ArchiveAsync(note.Id);

        var after = await sut.GetAllAsync();
        after.Should().ContainSingle().Which.NoteCount.Should().Be(0);
    }

    // ── RenameAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_UpdatesNameForEveryNoteUsingIt()
    {
        var (sut, notes, _, db) = BuildServices(nameof(RenameAsync_UpdatesNameForEveryNoteUsingIt));
        var tag = CreateTag(DefaultUserId, "meeting");
        db.Tags.Add(tag);
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: false, tag));
        await db.SaveChangesAsync();

        await sut.RenameAsync(tag.Id, "meetings");

        var stored = await db.Tags.AsNoTracking().SingleAsync();
        stored.Name.Should().Be("meetings");
        var note = await notes.GetByIdAsync((await db.Notes.AsNoTracking().SingleAsync()).Id);
        note!.Tags.Should().ContainSingle().Which.Should().Be("meetings");
    }

    [Fact]
    public async Task RenameAsync_TrimsWhitespace()
    {
        var (sut, _, _, db) = BuildServices(nameof(RenameAsync_TrimsWhitespace));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        await sut.RenameAsync(tag.Id, "  personal  ");

        (await db.Tags.AsNoTracking().SingleAsync()).Name.Should().Be("personal");
    }

    [Fact]
    public async Task RenameAsync_ToExistingName_ThrowsInvalidOperationException()
    {
        var (sut, _, _, db) = BuildServices(nameof(RenameAsync_ToExistingName_ThrowsInvalidOperationException));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        db.Tags.Add(CreateTag(DefaultUserId, "personal"));
        await db.SaveChangesAsync();

        var act = () => sut.RenameAsync(tag.Id, "personal");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Tags.AsNoTracking().SingleAsync(t => t.Id == tag.Id)).Name.Should().Be("work");
    }

    [Fact]
    public async Task RenameAsync_WithBlankName_ThrowsArgumentException()
    {
        var (sut, _, _, db) = BuildServices(nameof(RenameAsync_WithBlankName_ThrowsArgumentException));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        var act = () => sut.RenameAsync(tag.Id, "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RenameAsync_WhenTagBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, _, _, db) = BuildServices(nameof(RenameAsync_WhenTagBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateTag(OtherUserId, "work");
        db.Tags.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.RenameAsync(foreign.Id, "renamed");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RenameAsync_AfterListWasCached_RefreshesCollection()
    {
        var (sut, _, _, db) = BuildServices(nameof(RenameAsync_AfterListWasCached_RefreshesCollection));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        _ = await sut.GetAllAsync();

        await sut.RenameAsync(tag.Id, "career");
        var result = await sut.GetAllAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("career");
    }

    // ── MergeAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_MovesNoteAndResourceAssociationsToTargetAndRemovesSource()
    {
        var (sut, _, _, db) = BuildServices(nameof(MergeAsync_MovesNoteAndResourceAssociationsToTargetAndRemovesSource));
        var source = CreateTag(DefaultUserId, "meeting");
        var target = CreateTag(DefaultUserId, "meetings");
        db.Tags.Add(source);
        db.Tags.Add(target);
        var note = CreateNote(DefaultUserId, isArchived: false, source);
        var resource = CreateResource(DefaultUserId, isArchived: false, source);
        db.Notes.Add(note);
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        await sut.MergeAsync(source.Id, target.Id);

        (await db.Tags.AsNoTracking().ToListAsync()).Should().ContainSingle()
            .Which.Id.Should().Be(target.Id);

        var storedNote = await db.Notes.Include(n => n.Tags).AsNoTracking().SingleAsync();
        storedNote.Tags.Should().ContainSingle().Which.Id.Should().Be(target.Id);

        var storedResource = await db.Resources.Include(r => r.Tags).AsNoTracking().SingleAsync();
        storedResource.Tags.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task MergeAsync_WhenNoteAlreadyHasTargetTag_DoesNotDuplicateAssociation()
    {
        var (sut, _, _, db) = BuildServices(nameof(MergeAsync_WhenNoteAlreadyHasTargetTag_DoesNotDuplicateAssociation));
        var source = CreateTag(DefaultUserId, "meeting");
        var target = CreateTag(DefaultUserId, "meetings");
        db.Tags.Add(source);
        db.Tags.Add(target);
        var note = CreateNote(DefaultUserId, isArchived: false, source, target);
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        await sut.MergeAsync(source.Id, target.Id);

        var storedNote = await db.Notes.Include(n => n.Tags).AsNoTracking().SingleAsync();
        storedNote.Tags.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task MergeAsync_IntoSelf_ThrowsArgumentException()
    {
        var (sut, _, _, db) = BuildServices(nameof(MergeAsync_IntoSelf_ThrowsArgumentException));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        var act = () => sut.MergeAsync(tag.Id, tag.Id);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MergeAsync_WhenTargetBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, _, _, db) = BuildServices(nameof(MergeAsync_WhenTargetBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var source = CreateTag(DefaultUserId, "work");
        var foreignTarget = CreateTag(OtherUserId, "career");
        db.Tags.Add(source);
        db.Tags.Add(foreignTarget);
        await db.SaveChangesAsync();

        var act = () => sut.MergeAsync(source.Id, foreignTarget.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Tags.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MergeAsync_AfterListWasCached_RefreshesCollection()
    {
        var (sut, _, _, db) = BuildServices(nameof(MergeAsync_AfterListWasCached_RefreshesCollection));
        var source = CreateTag(DefaultUserId, "meeting");
        var target = CreateTag(DefaultUserId, "meetings");
        db.Tags.Add(source);
        db.Tags.Add(target);
        await db.SaveChangesAsync();
        _ = await sut.GetAllAsync();

        await sut.MergeAsync(source.Id, target.Id);
        var result = await sut.GetAllAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("meetings");
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DetachesTagWithoutDeletingNotesOrResources()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteAsync_DetachesTagWithoutDeletingNotesOrResources));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        var note = CreateNote(DefaultUserId, isArchived: false, tag);
        var resource = CreateResource(DefaultUserId, isArchived: false, tag);
        db.Notes.Add(note);
        db.Resources.Add(resource);
        await db.SaveChangesAsync();

        await sut.DeleteAsync(tag.Id);

        (await db.Tags.AsNoTracking().CountAsync()).Should().Be(0);
        (await db.Notes.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.Resources.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.Notes.Include(n => n.Tags).AsNoTracking().SingleAsync()).Tags.Should().BeEmpty();
        (await db.Resources.Include(r => r.Tags).AsNoTracking().SingleAsync()).Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_WhenTagBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteAsync_WhenTagBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreign = CreateTag(OtherUserId, "work");
        db.Tags.Add(foreign);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(foreign.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await db.Tags.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_AfterListWasCached_RefreshesCollection()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteAsync_AfterListWasCached_RefreshesCollection));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        var before = await sut.GetAllAsync();
        before.Should().ContainSingle();

        await sut.DeleteAsync(tag.Id);
        var after = await sut.GetAllAsync();

        after.Should().BeEmpty();
    }

    // ── DeleteUnusedAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUnusedAsync_RemovesOnlyTagsWithNoActiveUsage()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteUnusedAsync_RemovesOnlyTagsWithNoActiveUsage));
        var used = CreateTag(DefaultUserId, "work");
        var unused = CreateTag(DefaultUserId, "stale");
        var onlyArchived = CreateTag(DefaultUserId, "abandoned");
        db.Tags.Add(used);
        db.Tags.Add(unused);
        db.Tags.Add(onlyArchived);
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: false, used));
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: true, onlyArchived));
        await db.SaveChangesAsync();

        var removedCount = await sut.DeleteUnusedAsync();

        removedCount.Should().Be(2);
        var remaining = await db.Tags.AsNoTracking().ToListAsync();
        remaining.Should().ContainSingle().Which.Name.Should().Be("work");
    }

    [Fact]
    public async Task DeleteUnusedAsync_DoesNotAffectOtherUsersTags()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteUnusedAsync_DoesNotAffectOtherUsersTags));
        db.Tags.Add(CreateTag(OtherUserId, "theirs"));
        await db.SaveChangesAsync();

        var removedCount = await sut.DeleteUnusedAsync();

        removedCount.Should().Be(0);
        (await db.Tags.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteUnusedAsync_WhenNoneUnused_ReturnsZero()
    {
        var (sut, _, _, db) = BuildServices(nameof(DeleteUnusedAsync_WhenNoneUnused_ReturnsZero));
        var tag = CreateTag(DefaultUserId, "work");
        db.Tags.Add(tag);
        db.Notes.Add(CreateNote(DefaultUserId, isArchived: false, tag));
        await db.SaveChangesAsync();

        var removedCount = await sut.DeleteUnusedAsync();

        removedCount.Should().Be(0);
        (await db.Tags.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // ── Cross-service cache interaction ──────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_AfterNoteCreatedWithNewTagViaNoteService_IncludesNewTag()
    {
        var (sut, notes, _, _) = BuildServices(nameof(GetAllAsync_AfterNoteCreatedWithNewTagViaNoteService_IncludesNewTag));
        _ = await sut.GetAllAsync();

        await notes.CreateAsync(new CreateNoteDto("Title", "Body", Tags: ["fresh"]));

        var result = await sut.GetAllAsync();
        result.Should().ContainSingle(t => t.Name == "fresh" && t.NoteCount == 1);
    }
}
