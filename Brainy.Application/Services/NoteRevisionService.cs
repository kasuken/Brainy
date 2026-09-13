using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Reads, diffs and restores a note's revision history, all scoped to the current user.
/// See <see cref="NoteRevisionSupport"/> for where revisions are actually captured.
/// </summary>
internal sealed class NoteRevisionService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : INoteRevisionService
{
    public async Task<IReadOnlyList<NoteRevisionDto>> GetTimelineAsync(
        Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await context.Notes.EnsureOwnedAsync(noteId, userId, "Note", cancellationToken).ConfigureAwait(false);

        return await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.NoteId == noteId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteRevisionDto?> GetByIdAsync(
        Guid noteId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.Id == revisionId && r.NoteId == noteId && r.UserId == userId)
            .Select(r => ToDto(r))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteRevisionDiffDto> DiffAsync(
        Guid noteId, Guid fromRevisionId, Guid toRevisionId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var revisions = await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.NoteId == noteId && r.UserId == userId &&
                        (r.Id == fromRevisionId || r.Id == toRevisionId))
            .Select(r => new { r.Id, r.Title, r.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var from = revisions.FirstOrDefault(r => r.Id == fromRevisionId)
            ?? throw new KeyNotFoundException($"Revision '{fromRevisionId}' was not found.");
        var to = revisions.FirstOrDefault(r => r.Id == toRevisionId)
            ?? throw new KeyNotFoundException($"Revision '{toRevisionId}' was not found.");

        return new NoteRevisionDiffDto(
            fromRevisionId,
            toRevisionId,
            TextDiff.ComputeLineDiff(from.Title, to.Title),
            TextDiff.ComputeLineDiff(from.Content, to.Content));
    }

    public async Task<NoteDto> RestoreAsync(
        Guid noteId, Guid revisionId, byte[]? rowVersion, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        var revision = await context.NoteRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == revisionId && r.NoteId == noteId && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Revision '{revisionId}' was not found.");

        // Optimistic concurrency: compare against the token the caller loaded, so a note
        // edited elsewhere since then is detected as a conflict instead of restoring over it.
        if (rowVersion is not null)
            context.Entry(note).Property(n => n.RowVersion).OriginalValue = rowVersion;

        note.Title = revision.Title;
        note.Content = revision.Content;
        context.Entry(note).Property(n => n.UpdatedAtUtc).IsModified = true;

        context.NoteRevisions.Add(NoteRevisionSupport.BuildRevision(
            userId, noteId, revision.Title, revision.Content, NoteRevisionReason.Restore,
            restoredFromRevisionId: revision.Id));

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("note", ex);
        }

        await NoteRevisionSupport.TrimRetentionAsync(context, userId, noteId, CancellationToken.None)
            .ConfigureAwait(false);

        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(noteId),
                ApplicationCacheKey.EntityTypeTag<NoteRevision>()
            ],
            CancellationToken.None).ConfigureAwait(false);

        return ToNoteDto(note);
    }

    private static NoteRevisionDto ToDto(NoteRevision r) => new(
        r.Id, r.NoteId, r.Title, r.Content, r.Reason, r.CreatedAtUtc,
        r.Model, r.PromptVersion, r.RestoredFromRevisionId);

    private static NoteDto ToNoteDto(Note n) => new(
        n.Id,
        n.Title,
        n.Content,
        n.AiSummary,
        n.Status,
        n.IsArchived,
        n.ArchivedAtUtc,
        n.ProcessedAtUtc,
        n.ParaCategory,
        n.SourceId,
        n.ProjectId,
        n.AreaId,
        n.ResourceId,
        n.CreatedAtUtc,
        n.UpdatedAtUtc,
        n.IsFavorite,
        n.Images.Count > 0,
        SourceUrl: n.Source?.Url,
        SourceTitle: n.Source?.Title,
        RowVersion: n.RowVersion,
        Tags: n.Tags.Select(t => t.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
        ArchivedReason: n.ArchivedReason);
}
