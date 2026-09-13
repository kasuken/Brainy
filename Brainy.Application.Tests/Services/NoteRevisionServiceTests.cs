using AwesomeAssertions;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="INoteRevisionService"/> and the revision-capture side of
/// <see cref="INoteService"/>, resolved via the real DI container with an EF Core
/// InMemory database. Rowversion concurrency itself is NOT exercised here — EF InMemory
/// does not enforce it — see <c>Brainy.Data.IntegrationTests</c> for that against real
/// SQL Server.
/// </summary>
public class NoteRevisionServiceTests
{
    private const string DefaultUserId = "revision-user-1";
    private const string OtherUserId = "revision-user-2";

    private static (INoteService Notes, INoteRevisionService Revisions, BrainyDbContext Db) BuildServices(
        string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<INoteService>(),
            provider.GetRequiredService<INoteRevisionService>(),
            provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task CreateAsync_CapturesInitialRevisionWithUserEditReason()
    {
        var (notes, revisions, _) = BuildServices(nameof(CreateAsync_CapturesInitialRevisionWithUserEditReason));

        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Body"));
        var timeline = await revisions.GetTimelineAsync(note.Id);

        timeline.Should().ContainSingle();
        timeline[0].Title.Should().Be("Title");
        timeline[0].Content.Should().Be("Body");
        timeline[0].Reason.Should().Be(NoteRevisionReason.UserEdit);
    }

    [Fact]
    public async Task CreateAsync_WithExplicitChangeReason_UsesIt()
    {
        var (notes, revisions, _) = BuildServices(nameof(CreateAsync_WithExplicitChangeReason_UsesIt));

        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Body", ChangeReason: NoteRevisionReason.Import));
        var timeline = await revisions.GetTimelineAsync(note.Id);

        timeline.Should().ContainSingle().Which.Reason.Should().Be(NoteRevisionReason.Import);
    }

    [Fact]
    public async Task UpdateAsync_WhenTitleOrContentChanges_AppendsANewRevision()
    {
        var (notes, revisions, _) = BuildServices(nameof(UpdateAsync_WhenTitleOrContentChanges_AppendsANewRevision));
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Body"));

        await notes.UpdateAsync(new UpdateNoteDto(
            note.Id, "Title", "Edited body", null, note.Status, note.ParaCategory,
            null, null, null, RowVersion: note.RowVersion));

        var timeline = await revisions.GetTimelineAsync(note.Id);
        timeline.Should().HaveCount(2);
        timeline[0].Content.Should().Be("Edited body");
        timeline[1].Content.Should().Be("Body");
    }

    [Fact]
    public async Task UpdateAsync_WhenTitleAndContentUnchanged_DoesNotAppendARevision()
    {
        var (notes, revisions, _) = BuildServices(nameof(UpdateAsync_WhenTitleAndContentUnchanged_DoesNotAppendARevision));
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Body"));

        // Only the PARA category changes; title/content are resubmitted unchanged.
        await notes.UpdateAsync(new UpdateNoteDto(
            note.Id, "Title", "Body", null, note.Status, ParaCategory.Area,
            null, null, null, RowVersion: note.RowVersion));

        var timeline = await revisions.GetTimelineAsync(note.Id);
        timeline.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTimelineAsync_ForAnotherUsersNote_Throws()
    {
        var (notes, _, _) = BuildServices(nameof(GetTimelineAsync_ForAnotherUsersNote_Throws), DefaultUserId);
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Body"));

        var (_, otherUsersRevisions, _) = BuildServices(nameof(GetTimelineAsync_ForAnotherUsersNote_Throws), OtherUserId);

        var act = () => otherUsersRevisions.GetTimelineAsync(note.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DiffAsync_ReturnsLineLevelAddedAndRemovedLines()
    {
        var (notes, revisions, _) = BuildServices(nameof(DiffAsync_ReturnsLineLevelAddedAndRemovedLines));
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "line one\nline two"));
        await notes.UpdateAsync(new UpdateNoteDto(
            note.Id, "Title", "line one\nline three", null, note.Status, note.ParaCategory,
            null, null, null, RowVersion: note.RowVersion));

        var timeline = await revisions.GetTimelineAsync(note.Id);
        var newest = timeline[0];
        var oldest = timeline[1];

        var diff = await revisions.DiffAsync(note.Id, oldest.Id, newest.Id);

        diff.ContentDiff.Should().Contain(line =>
            line.Kind == NoteRevisionDiffLineKind.Unchanged && line.Text == "line one");
        diff.ContentDiff.Should().Contain(line =>
            line.Kind == NoteRevisionDiffLineKind.Removed && line.Text == "line two");
        diff.ContentDiff.Should().Contain(line =>
            line.Kind == NoteRevisionDiffLineKind.Added && line.Text == "line three");
    }

    [Fact]
    public async Task RestoreAsync_AppliesOldRevisionAsANewRevision_WithoutOverwritingHistory()
    {
        var (notes, revisions, _) = BuildServices(
            nameof(RestoreAsync_AppliesOldRevisionAsANewRevision_WithoutOverwritingHistory));
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "Original body"));
        var updated = await notes.UpdateAsync(new UpdateNoteDto(
            note.Id, "Title", "Edited body", null, note.Status, note.ParaCategory,
            null, null, null, RowVersion: note.RowVersion));

        var timelineBeforeRestore = await revisions.GetTimelineAsync(note.Id);
        var originalRevision = timelineBeforeRestore.Should().Contain(r => r.Content == "Original body").Which;

        var restored = await revisions.RestoreAsync(note.Id, originalRevision.Id, updated.RowVersion);

        restored.Content.Should().Be("Original body");

        var timelineAfterRestore = await revisions.GetTimelineAsync(note.Id);
        timelineAfterRestore.Should().HaveCount(3, "restore appends a new revision instead of rewriting history");
        var restoredRevision = timelineAfterRestore[0];
        restoredRevision.Reason.Should().Be(NoteRevisionReason.Restore);
        restoredRevision.RestoredFromRevisionId.Should().Be(originalRevision.Id);
        restoredRevision.Content.Should().Be("Original body");

        // The two earlier revisions are still there, untouched.
        timelineAfterRestore.Should().Contain(r => r.Id == originalRevision.Id && r.Content == "Original body");
        timelineAfterRestore.Should().Contain(r => r.Content == "Edited body");
    }

    [Fact]
    public async Task RestoreAsync_ForAnotherUsersNote_Throws()
    {
        var dbName = nameof(RestoreAsync_ForAnotherUsersNote_Throws);
        var (ownerNotes, ownerRevisions, _) = BuildServices(dbName, DefaultUserId);
        var note = await ownerNotes.CreateAsync(new CreateNoteDto("Title", "Body"));
        var timeline = await ownerRevisions.GetTimelineAsync(note.Id);

        var (_, otherRevisions, _) = BuildServices(dbName, OtherUserId);

        var act = () => otherRevisions.RestoreAsync(note.Id, timeline[0].Id, null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_BeyondHardCap_TrimsOldestRevisionsButKeepsTheNewest()
    {
        var dbName = nameof(Update_BeyondHardCap_TrimsOldestRevisionsButKeepsTheNewest);
        var (notes, revisions, _) = BuildServices(dbName);
        var note = await notes.CreateAsync(new CreateNoteDto("Title", "v0"));

        // One initial revision plus enough edits to exceed the hard cap by a few.
        var current = note;
        for (var i = 1; i <= INoteRevisionService.MaxRevisionsPerNote + 5; i++)
        {
            current = await notes.UpdateAsync(new UpdateNoteDto(
                note.Id, "Title", $"v{i}", null, current.Status, current.ParaCategory,
                null, null, null, RowVersion: current.RowVersion));
        }

        var timeline = await revisions.GetTimelineAsync(note.Id);

        timeline.Count.Should().Be(INoteRevisionService.MaxRevisionsPerNote);
        timeline[0].Content.Should().Be($"v{INoteRevisionService.MaxRevisionsPerNote + 5}");
        timeline.Should().NotContain(r => r.Content == "v0", "the oldest revisions are purged past the hard cap");
    }
}
