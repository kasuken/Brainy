using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Shared revision-capture and retention-trim logic used by <see cref="NoteService"/>
/// (user edits, processing), <see cref="ExternalImport.ExternalImportService"/> (imports,
/// which write notes directly rather than through <see cref="NoteService"/>), and
/// <see cref="NoteRevisionService"/> (restore). Kept as a static helper — rather than a
/// service the other two would inject — so revision rows are always added to the SAME
/// <see cref="IApplicationDbContext"/> unit of work as the note write they accompany: if
/// that unit of work's <c>SaveChangesAsync</c> is rejected by the rowversion concurrency
/// check, the revision is rolled back with it instead of silently forking history.
/// </summary>
internal static class NoteRevisionSupport
{
    public static NoteRevision BuildRevision(
        string userId,
        Guid noteId,
        string title,
        string content,
        NoteRevisionReason reason,
        string? model = null,
        string? promptVersion = null,
        Guid? restoredFromRevisionId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        NoteId = noteId,
        Title = title,
        Content = content,
        Reason = reason,
        Model = model,
        PromptVersion = promptVersion,
        RestoredFromRevisionId = restoredFromRevisionId
    };

    /// <summary>
    /// Enforces bounded storage for one note's revision history: deletes revisions older
    /// than the user's configured <c>NoteRevision</c> retention rule (if any), and — always,
    /// regardless of whether a rule is configured — trims down to at most
    /// <see cref="INoteRevisionService.MaxRevisionsPerNote"/> rows. The single most recent
    /// revision is never purged, so a note's history is never emptied entirely. Runs after
    /// the triggering save has already committed, so a failed or skipped trim never risks
    /// the note/revision write itself.
    /// </summary>
    public static async Task TrimRetentionAsync(
        IApplicationDbContext context, string userId, Guid noteId, CancellationToken cancellationToken)
    {
        var revisionIds = await context.NoteRevisions
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.NoteId == noteId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.CreatedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (revisionIds.Count <= 1)
            return;

        var toDelete = new HashSet<Guid>();

        // Age-based: only past the configured NoteRevision retention rule, if any.
        var rule = await context.ArchiveRetentionRules
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.EntityType == NoteRevisionEntityType)
            .Select(r => r.RetentionDays)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rule is { } retentionDays)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var revision in revisionIds.Skip(1).Where(r => r.CreatedAtUtc < cutoff))
                toDelete.Add(revision.Id);
        }

        // Count-based hard cap: always applied, independent of any configured rule.
        if (revisionIds.Count > INoteRevisionService.MaxRevisionsPerNote)
        {
            foreach (var revision in revisionIds.Skip(INoteRevisionService.MaxRevisionsPerNote))
                toDelete.Add(revision.Id);
        }

        if (toDelete.Count == 0)
            return;

        await context.NoteRevisions
            .Where(r => r.UserId == userId && r.NoteId == noteId && toDelete.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The <see cref="ArchiveRetentionRule.EntityType"/> key used for revision retention.</summary>
    public const string NoteRevisionEntityType = "NoteRevision";
}
