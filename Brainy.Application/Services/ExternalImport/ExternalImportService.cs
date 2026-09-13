using Brainy.Application.DTOs.DataImport;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Imports notes from a non-Brainy source (Obsidian, Notion, a generic Markdown folder,
/// or Evernote) for the current user. Each format's own parser (see
/// <see cref="MarkdownVaultParser"/>, <see cref="NotionExportParser"/>,
/// <see cref="EvernoteEnexParser"/>) reduces the source to a flat list of
/// <see cref="ParsedImportNote"/>s; this class then reconciles that list against the
/// user's existing data using the same shape of plan <c>DataImportService</c> uses for
/// Brainy's own JSON export: creates / reuses / skips per entity type, computed identically
/// whether previewing (nothing written) or committing (one transaction, all or nothing).
///
/// Every imported note lands in the Inbox (<see cref="NoteStatus.Inbox"/>,
/// <see cref="ParaCategory.Project"/> — the same default Quick Capture and the share-sheet
/// capture path use) rather than a guessed PARA category, so the user files it themselves.
///
/// Duplicate-safety: a note is matched against the user's existing notes by a
/// content-based key (normalized title + body with every image/embed reference stripped,
/// so re-importing after an attachment picks up a freshly-minted image id still matches).
/// A matched note is reused as-is; tags, attachments and relationships are only ever
/// established for a note created in this run, mirroring the "known narrower gap" the
/// JSON importer accepts for the same reason (see <c>DataImportService</c> remarks).
/// </summary>
internal sealed class ExternalImportService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IExternalImportService
{
    /// <summary>Hard cap on the uploaded file itself, before any archive/XML parsing begins.</summary>
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    private const int MaxTagNameLength = 100;
    private const int MaxTitleLength = 500;

    public async Task<ExternalImportPreviewDto> PreviewImportAsync(
        ExternalImportSourceFormat format, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await ParseAsync(format, content, cancellationToken).ConfigureAwait(false);

        var state = new ImportState(userId, commit: false);
        await RunPlanAsync(parsed.Notes, state, cancellationToken).ConfigureAwait(false);
        Classify(parsed.Warnings, state);

        return new ExternalImportPreviewDto(FormatLabel(format), state.BuildOutcomes(), BuildUnmapped(parsed.Warnings), state.Conflicts, state.IntegrityIssues);
    }

    public async Task<ExternalImportResultDto> ImportCurrentUserAsync(
        ExternalImportSourceFormat format, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await ParseAsync(format, content, cancellationToken).ConfigureAwait(false);

        var state = await context.ExecuteInTransactionAsync(async ct =>
        {
            var importState = new ImportState(userId, commit: true);
            await RunPlanAsync(parsed.Notes, importState, ct).ConfigureAwait(false);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return importState;
        }, cancellationToken).ConfigureAwait(false);
        Classify(parsed.Warnings, state);

        await cache.InvalidateUserAsync(userId, CancellationToken.None).ConfigureAwait(false);

        return new ExternalImportResultDto(FormatLabel(format), state.BuildOutcomes(), BuildUnmapped(parsed.Warnings), state.Conflicts, state.IntegrityIssues);
    }

    private static async Task<ExternalImportParseResult> ParseAsync(
        ExternalImportSourceFormat format, Stream content, CancellationToken cancellationToken)
    {
        await using var buffered = await BufferBoundedAsync(content, MaxUploadBytes, cancellationToken).ConfigureAwait(false);

        return format switch
        {
            ExternalImportSourceFormat.ObsidianVault => MarkdownVaultParser.Parse(buffered),
            ExternalImportSourceFormat.MarkdownFolder => MarkdownVaultParser.Parse(buffered),
            ExternalImportSourceFormat.NotionExport => NotionExportParser.Parse(buffered),
            ExternalImportSourceFormat.Evernote => EvernoteEnexParser.Parse(buffered),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown import source format.")
        };
    }

    /// <summary>
    /// Copies the upload into a seekable in-memory buffer (zip reading needs to seek to
    /// the central directory) while enforcing a hard byte cap — rejecting an oversized
    /// upload before any archive/XML parsing begins, rather than after decompressing it.
    /// </summary>
    private static async Task<MemoryStream> BufferBoundedAsync(Stream input, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;

        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"The uploaded file exceeds the {maxBytes / (1024 * 1024)} MB limit for this import.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return buffer;
    }

    private async Task RunPlanAsync(IReadOnlyList<ParsedImportNote> notes, ImportState state, CancellationToken cancellationToken)
    {
        var existingNotes = await context.Notes.AsNoTracking()
            .Where(note => note.UserId == state.UserId)
            .Select(note => new { note.Id, note.Title, note.Content })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByDedupKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var note in existingNotes)
            existingByDedupKey[ComputeDedupKey(note.Title, note.Content)] = note.Id;

        var existingTags = await context.Tags
            .Where(tag => tag.UserId == state.UserId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var tagByName = existingTags.ToDictionary(tag => tag.Name, tag => tag, StringComparer.OrdinalIgnoreCase);

        var existingRelationshipKeys = (await context.NoteRelationships.AsNoTracking()
            .Where(r => r.SourceNote.UserId == state.UserId && r.TargetNote.UserId == state.UserId)
            .Select(r => new { r.SourceNoteId, r.TargetNoteId, r.Type })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(r => (r.SourceNoteId, r.TargetNoteId, r.Type))
            .ToHashSet();

        int notesCreated = 0, notesReused = 0, notesSkipped = 0;
        int tagsCreated = 0, tagsReused = 0;
        int imagesCreated = 0, imagesSkipped = 0;

        foreach (var parsedNote in notes)
        {
            var title = NormalizeTitle(parsedNote.Title);
            if (title is null)
            {
                notesSkipped++;
                continue;
            }

            var dedupKey = ComputeDedupKey(title, parsedNote.Content);
            if (existingByDedupKey.TryGetValue(dedupKey, out var existingId))
            {
                state.NoteKeyToId[parsedNote.Key] = existingId;
                notesReused++;
                continue;
            }

            var noteId = Guid.NewGuid();
            var tags = new List<Tag>();
            var seenTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawTagName in parsedNote.Tags)
            {
                var tagName = rawTagName.Trim();
                if (tagName.Length == 0 || tagName.Length > MaxTagNameLength) continue;
                if (!seenTagNames.Add(tagName)) continue;

                if (tagByName.TryGetValue(tagName, out var existingTag))
                {
                    tags.Add(existingTag);
                    tagsReused++;
                    continue;
                }

                var tag = new Tag { Id = Guid.NewGuid(), UserId = state.UserId, Name = tagName };
                if (state.Commit) context.Tags.Add(tag);
                tagByName[tagName] = tag;
                tags.Add(tag);
                tagsCreated++;
            }

            var finalContent = parsedNote.Content;
            foreach (var attachment in parsedNote.Attachments)
            {
                if (attachment.Data is null || attachment.ContentType is null)
                {
                    imagesSkipped++;
                    continue;
                }

                var image = new NoteImage
                {
                    Id = Guid.NewGuid(),
                    UserId = state.UserId,
                    NoteId = noteId,
                    FileName = attachment.FileName.Length > 255 ? attachment.FileName[..255] : attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.Data.LongLength,
                    Data = attachment.Data
                };

                if (state.Commit) context.NoteImages.Add(image);
                finalContent = finalContent.Replace(
                    attachment.RawToken, $"![{image.FileName}](/api/note-images/{image.Id})", StringComparison.Ordinal);
                imagesCreated++;
            }

            var note = new Note
            {
                Id = noteId,
                UserId = state.UserId,
                Title = title,
                Content = finalContent,
                Status = NoteStatus.Inbox,
                ParaCategory = ParaCategory.Project,
                Tags = tags
            };

            if (state.Commit) context.Notes.Add(note);
            state.NoteKeyToId[parsedNote.Key] = noteId;
            existingByDedupKey[dedupKey] = noteId;
            notesCreated++;
        }

        int relationshipsCreated = 0, relationshipsSkipped = 0;
        foreach (var parsedNote in notes)
        {
            if (!state.NoteKeyToId.TryGetValue(parsedNote.Key, out var sourceId))
                continue;

            foreach (var targetKey in parsedNote.LinkTargetKeys)
            {
                if (!state.NoteKeyToId.TryGetValue(targetKey, out var targetId) || targetId == sourceId)
                {
                    relationshipsSkipped++;
                    continue;
                }

                if (!existingRelationshipKeys.Add((sourceId, targetId, RelationshipType.Related)))
                {
                    relationshipsSkipped++;
                    continue;
                }

                var relationship = new NoteRelationship
                {
                    Id = Guid.NewGuid(),
                    SourceNoteId = sourceId,
                    TargetNoteId = targetId,
                    Type = RelationshipType.Related
                };

                if (state.Commit) context.NoteRelationships.Add(relationship);
                relationshipsCreated++;
            }
        }

        state.RecordOutcome("Notes", notesCreated, notesReused, notesSkipped);
        state.RecordOutcome("Tags", tagsCreated, tagsReused, 0);
        state.RecordOutcome("Note images", imagesCreated, 0, imagesSkipped);
        state.RecordOutcome("Note relationships", relationshipsCreated, 0, relationshipsSkipped);
    }

    private static string? NormalizeTitle(string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return null;
        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }

    private static string ComputeDedupKey(string title, string content) =>
        title.Trim().ToLowerInvariant() + "" + MarkdownTextHelpers.NormalizeForDedup(content);

    private static void Classify(IReadOnlyList<string> warnings, ImportState state)
    {
        foreach (var warning in warnings)
        {
            if (warning.StartsWith("Multiple notes are named", StringComparison.Ordinal))
                state.Conflicts.Add(warning);
            else
                state.IntegrityIssues.Add(warning);
        }
    }

    private static IReadOnlyList<DataImportEntityCountDto> BuildUnmapped(IReadOnlyList<string> warnings)
    {
        var result = new List<DataImportEntityCountDto>();

        var rejectedArchiveEntries = warnings.Count(w => w.Contains("': rejected (", StringComparison.Ordinal));
        if (rejectedArchiveEntries > 0)
            result.Add(new DataImportEntityCountDto("Archive entries rejected for safety", rejectedArchiveEntries));

        var unresolvedAttachments = warnings.Count(w => w.StartsWith("Attachment '", StringComparison.Ordinal));
        if (unresolvedAttachments > 0)
            result.Add(new DataImportEntityCountDto("Attachments not imported", unresolvedAttachments));

        return result;
    }

    private static string FormatLabel(ExternalImportSourceFormat format) => format switch
    {
        ExternalImportSourceFormat.ObsidianVault => "Obsidian vault",
        ExternalImportSourceFormat.NotionExport => "Notion export",
        ExternalImportSourceFormat.MarkdownFolder => "Markdown folder",
        ExternalImportSourceFormat.Evernote => "Evernote export",
        _ => format.ToString()
    };

    private sealed class ImportState(string userId, bool commit)
    {
        public string UserId { get; } = userId;
        public bool Commit { get; } = commit;
        public Dictionary<string, Guid> NoteKeyToId { get; } = new(StringComparer.Ordinal);
        public List<string> Conflicts { get; } = [];
        public List<string> IntegrityIssues { get; } = [];

        private readonly List<DataImportEntityOutcomeDto> _outcomes = [];

        public void RecordOutcome(string entityType, int created, int reused, int skipped) =>
            _outcomes.Add(new DataImportEntityOutcomeDto(entityType, created, reused, skipped));

        public IReadOnlyList<DataImportEntityOutcomeDto> BuildOutcomes() => _outcomes;
    }
}
