using Brainy.Application.Common;
using Brainy.Application.Caching;
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
/// Handles CRUD operations for <see cref="Note"/> entities, scoped to the current user.
/// Reads use <c>AsNoTracking</c> for performance; writes load tracked entities.
/// </summary>
internal sealed class NoteService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : INoteService
{
    private const int MaxTagsPerNote = 20;
    private const int MaxTagNameLength = 100;

    public async Task<IReadOnlyList<NoteDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "notes:all",
            NoteReadTags(),
            async ct => await ProjectNotes(
                    context.Notes
                        .AsNoTracking()
                        .Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.UpdatedAtUtc),
                    userId)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"notes:{id}",
            NoteReadTags(id),
            ct => ProjectNotes(
                    context.Notes
                        .AsNoTracking()
                        .Where(n => n.Id == id && n.UserId == userId),
                    userId)
                .FirstOrDefaultAsync(ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteDto> CreateAsync(CreateNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.EnsureNoteLinksOwnedAsync(
            userId, dto.ProjectId, dto.AreaId, dto.ResourceId, cancellationToken).ConfigureAwait(false);

        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = dto.Title,
            Content = dto.Content,
            Status = dto.Status,
            ParaCategory = dto.ParaCategory,
            ProjectId = dto.ProjectId,
            AreaId = dto.AreaId,
            ResourceId = dto.ResourceId
        };

        if (!string.IsNullOrWhiteSpace(dto.SourceUrl))
        {
            var source = new Source
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = SourceType.Url,
                Url = dto.SourceUrl.Trim(),
                Title = string.IsNullOrWhiteSpace(dto.SourceTitle) ? null : dto.SourceTitle.Trim(),
                CapturedAtUtc = DateTime.UtcNow
            };
            context.Sources.Add(source);
            note.Source = source;
        }

        note.Tags = await ResolveTagsAsync(userId, dto.Tags ?? [], cancellationToken).ConfigureAwait(false);

        context.Notes.Add(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(
            userId,
            note.Id,
            note.Tags.Select(tag => tag.Id),
            note.SourceId.HasValue ? [note.SourceId.Value] : []).ConfigureAwait(false);

        return ToDto(note);
    }

    public async Task<NoteDto> UpdateAsync(UpdateNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == dto.Id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.Id}' was not found.");
        var sourceIds = note.SourceId.HasValue
            ? new HashSet<Guid> { note.SourceId.Value }
            : [];

        await context.EnsureNoteLinksOwnedAsync(
            userId, dto.ProjectId, dto.AreaId, dto.ResourceId, cancellationToken).ConfigureAwait(false);

        // Optimistic concurrency: compare against the token captured when the caller
        // loaded the note, not the freshly loaded one, so edits made in another
        // tab/circuit since then are detected instead of silently overwritten.
        if (dto.RowVersion is not null)
            context.Entry(note).Property(n => n.RowVersion).OriginalValue = dto.RowVersion;

        note.Title = dto.Title;
        note.Content = dto.Content;
        note.AiSummary = dto.AiSummary;
        note.Status = dto.Status;
        note.ParaCategory = dto.ParaCategory;
        note.ProjectId = dto.ProjectId;
        note.AreaId = dto.AreaId;
        note.ResourceId = dto.ResourceId;

        if (dto.Tags is not null)
            note.Tags = await ResolveTagsAsync(userId, dto.Tags, cancellationToken).ConfigureAwait(false);

        // Force one Note UPDATE even when only tag join rows changed so the rowversion
        // predicate is checked and the note's audit timestamp advances.
        context.Entry(note).Property(n => n.UpdatedAtUtc).IsModified = true;

        if (dto.Status == NoteStatus.Archived)
        {
            note.IsArchived = true;
            note.ArchivedAtUtc ??= DateTime.UtcNow;
        }
        else if (note.IsArchived)
        {
            note.IsArchived = false;
            note.ArchivedAtUtc = null;
        }

        // SourceUrl == null  → leave existing source untouched
        // SourceUrl == ""    → clear the source link
        // SourceUrl has value → create or update the linked Source entity
        if (dto.SourceUrl is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceUrl))
            {
                note.SourceId = null;
                note.Source = null;
            }
            else if (note.Source is not null)
            {
                note.Source.Url = dto.SourceUrl.Trim();
                note.Source.Title = string.IsNullOrWhiteSpace(dto.SourceTitle)
                    ? null
                    : dto.SourceTitle.Trim();
            }
            else
            {
                var source = new Source
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Type = SourceType.Url,
                    Url = dto.SourceUrl.Trim(),
                    Title = string.IsNullOrWhiteSpace(dto.SourceTitle) ? null : dto.SourceTitle.Trim(),
                    CapturedAtUtc = DateTime.UtcNow
                };
                context.Sources.Add(source);
                note.Source = source;
            }
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("note", ex);
        }

        if (note.SourceId.HasValue)
            sourceIds.Add(note.SourceId.Value);
        await InvalidateNoteAsync(
            userId,
            note.Id,
            note.Tags.Select(tag => tag.Id),
            sourceIds).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task<IReadOnlyList<NoteDto>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "notes:inbox",
            NoteReadTags(),
            async ct => await ProjectNotes(
                    context.Notes
                        .AsNoTracking()
                        .Where(n => n.UserId == userId && n.Status == NoteStatus.Inbox && !n.IsArchived)
                        .OrderBy(n => n.CreatedAtUtc),
                    userId)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteDto> ProcessNoteAsync(ProcessNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == dto.Id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.Id}' was not found.");

        await context.EnsureNoteLinksOwnedAsync(
            userId, dto.ProjectId, dto.AreaId, dto.ResourceId, cancellationToken).ConfigureAwait(false);

        if (dto.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("A note title is required.", nameof(dto));

            note.Title = dto.Title.Trim();
        }

        if (dto.Content is not null)
            note.Content = dto.Content;

        if (dto.RowVersion is not null)
            context.Entry(note).Property(n => n.RowVersion).OriginalValue = dto.RowVersion;

        note.Status          = dto.Status;
        note.ParaCategory    = dto.ParaCategory;
        note.ProjectId       = dto.ProjectId;
        note.AreaId          = dto.AreaId;
        note.ResourceId      = dto.ResourceId;
        note.ProcessedAtUtc  = DateTime.UtcNow;

        if (dto.Status == NoteStatus.Archived)
        {
            note.IsArchived = true;
            note.ArchivedAtUtc ??= DateTime.UtcNow;
        }
        else if (note.IsArchived)
        {
            note.IsArchived = false;
            note.ArchivedAtUtc = null;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("note", ex);
        }

        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task<int> BulkProcessInboxAsync(
        IEnumerable<Guid> ids,
        ParaCategory category,
        NoteStatus status,
        Guid? projectId = null,
        Guid? areaId = null,
        Guid? resourceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids as ICollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.EnsureNoteLinksOwnedAsync(
            userId, projectId, areaId, resourceId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var isArchived = status == NoteStatus.Archived;

        var notes = await context.Notes
            .Where(n => n.UserId == userId && idList.Contains(n.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var note in notes)
        {
            note.ParaCategory = category;
            note.Status = status;
            note.ProjectId = projectId;
            note.AreaId = areaId;
            note.ResourceId = resourceId;
            note.IsArchived = isArchived;
            note.ArchivedAtUtc = isArchived ? now : null;
            note.ProcessedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNotesAsync(userId, notes.Select(note => note.Id)).ConfigureAwait(false);
        return notes.Count;
    }

    public async Task<int> BulkMoveCategoryAsync(IEnumerable<Guid> ids, ParaCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids as ICollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var updated = await context.Notes
            .Where(n => n.UserId == userId && idList.Contains(n.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.ParaCategory, category)
                    .SetProperty(n => n.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated > 0)
            await InvalidateNotesAsync(userId, idList).ConfigureAwait(false);
        return updated;
    }

    public async Task<IReadOnlyList<NoteDto>> GetNotLinkedToProjectAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "notes:without-project",
            NoteReadTags(),
            async ct => await ProjectNotes(
                    context.Notes
                        .AsNoTracking()
                        .Where(n => n.UserId == userId && n.ProjectId == null && n.Status != NoteStatus.Archived)
                        .OrderByDescending(n => n.UpdatedAtUtc),
                    userId)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteDto> LinkToProjectAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Projects.EnsureOwnedAsync(projectId, userId, "Project", cancellationToken)
            .ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ProjectId = projectId;

        // Only upgrade category if it is not already categorised as a project item
        if (note.ParaCategory != ParaCategory.Project && note.ParaCategory != ParaCategory.Resource)
            note.ParaCategory = ParaCategory.Project;

        // Promote inbox notes to Active
        if (note.Status == NoteStatus.Inbox)
            note.Status = NoteStatus.Active;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task<NoteDto> UnlinkFromProjectAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ProjectId = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");

        // Incoming relationship links use Restrict delete behaviour, so links in both
        // directions must be removed explicitly before the note can be deleted.
        var relationshipLinks = await context.NoteRelationships
            .Where(r => r.SourceNoteId == id || r.TargetNoteId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.NoteRelationships.RemoveRange(relationshipLinks);

        context.Notes.Remove(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        List<string> changedTags =
        [
            ApplicationCacheKey.EntityTypeTag<Note>(),
            ApplicationCacheKey.EntityTag<Note>(note.Id),
            ApplicationCacheKey.EntityTypeTag<NoteRelationship>(),
            ApplicationCacheKey.EntityTypeTag<NoteImage>(),
            ApplicationCacheKey.EntityTypeTag<Highlight>(),
            ApplicationCacheKey.EntityTypeTag<Summary>(),
            ApplicationCacheKey.EntityTypeTag<ActionItem>(),
            ApplicationCacheKey.EntityTypeTag<Output>()
        ];
        changedTags.AddRange(relationshipLinks.Select(
            relationship => ApplicationCacheKey.EntityTag<NoteRelationship>(relationship.Id)));
        await cache.InvalidateTagsAsync(userId, changedTags, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NoteDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await cache.GetOrCreateAsync(
            userId,
            "notes:archived",
            NoteReadTags(),
            async ct => await ProjectNotes(
                    context.Notes
                        .AsNoTracking()
                        .Where(n => n.UserId == userId && (n.IsArchived || n.Status == NoteStatus.Archived))
                        .OrderByDescending(n => n.ArchivedAtUtc ?? n.UpdatedAtUtc),
                    userId)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");
        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        note.IsArchived = true;
        note.Status = NoteStatus.Archived;
        note.ParaCategory = ParaCategory.Archive;
        note.ArchivedAtUtc = DateTime.UtcNow;
        note.ArchivedReason = normalizedReason;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(note.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);
        note.IsArchived = false;
        if (note.Status == NoteStatus.Archived)
        {
            note.Status = NoteStatus.Active;
        }
        note.ArchivedAtUtc = null;
        note.ArchivedReason = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves normalized tag names against only the current user's tag rows, reusing
    /// tags shared with Resources and creating missing names when necessary.
    /// </summary>
    private async Task<List<Tag>> ResolveTagsAsync(
        string userId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken)
    {
        var normalizedNames = new List<string>(tagNames.Count);

        foreach (var tagName in tagNames)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                continue;

            var normalized = tagName.Trim();
            if (normalized.Length > MaxTagNameLength)
                throw new ArgumentException($"Tag names cannot exceed {MaxTagNameLength} characters.", nameof(tagNames));

            if (normalized.Any(char.IsControl))
                throw new ArgumentException("Tag names cannot contain control characters.", nameof(tagNames));

            if (!normalizedNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                normalizedNames.Add(normalized);
        }

        if (normalizedNames.Count > MaxTagsPerNote)
            throw new ArgumentException($"A note can have at most {MaxTagsPerNote} tags.", nameof(tagNames));

        if (normalizedNames.Count == 0)
            return [];

        var normalizedKeys = normalizedNames.Select(name => name.ToLowerInvariant()).ToList();
        var existing = await context.Tags
            .Where(tag => tag.UserId == userId && normalizedKeys.Contains(tag.Name.ToLower()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByName = existing
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<Tag>(normalizedNames.Count);
        foreach (var name in normalizedNames)
        {
            if (existingByName.TryGetValue(name, out var existingTag))
            {
                resolved.Add(existingTag);
                continue;
            }

            var tag = new Tag
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name
            };
            context.Tags.Add(tag);
            existingByName[name] = tag;
            resolved.Add(tag);
        }

        return resolved;
    }

    private static NoteDto ToDto(Note n) => new(
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

    private static IQueryable<NoteDto> ProjectNotes(IQueryable<Note> notes, string userId) =>
        notes
            .Select(n => new NoteDto(
                n.Id, n.Title, n.Content, n.AiSummary, n.Status, n.IsArchived,
                n.ArchivedAtUtc, n.ProcessedAtUtc, n.ParaCategory, n.SourceId,
                n.ProjectId, n.AreaId, n.ResourceId, n.CreatedAtUtc, n.UpdatedAtUtc,
                n.IsFavorite, n.Images.Any(),
                SourceUrl: n.Source != null ? n.Source.Url : null,
                SourceTitle: n.Source != null ? n.Source.Title : null,
                RowVersion: n.RowVersion,
                Tags: n.Tags
                    .Where(t => t.UserId == userId)
                    .Select(t => t.Name)
                    .OrderBy(name => name)
                    .ToList(),
                ArchivedReason: n.ArchivedReason));

    private static IReadOnlyCollection<string> NoteReadTags(Guid? noteId = null)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Note>(),
            ApplicationCacheKey.EntityTypeTag<NoteImage>(),
            ApplicationCacheKey.EntityTypeTag<Source>(),
            ApplicationCacheKey.EntityTypeTag<Tag>()
        ];
        if (noteId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<Note>(noteId.Value));
        return tags;
    }

    private ValueTask InvalidateNoteAsync(
        string userId,
        Guid noteId,
        IEnumerable<Guid>? tagIds = null,
        IEnumerable<Guid>? sourceIds = null)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Note>(),
            ApplicationCacheKey.EntityTag<Note>(noteId),
            ApplicationCacheKey.EntityTypeTag<LifecycleActivity>()
        ];

        if (tagIds is not null)
        {
            tags.Add(ApplicationCacheKey.EntityTypeTag<Tag>());
            tags.AddRange(tagIds.Select(ApplicationCacheKey.EntityTag<Tag>));
        }

        if (sourceIds is not null)
        {
            tags.Add(ApplicationCacheKey.EntityTypeTag<Source>());
            tags.AddRange(sourceIds.Select(ApplicationCacheKey.EntityTag<Source>));
        }

        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private ValueTask InvalidateNotesAsync(string userId, IEnumerable<Guid> noteIds)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Note>(),
            ApplicationCacheKey.EntityTypeTag<LifecycleActivity>()
        ];
        tags.AddRange(noteIds.Select(ApplicationCacheKey.EntityTag<Note>));
        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    public async Task<NoteDto> ToggleFavoriteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .Include(n => n.Source)
            .Include(n => n.Tags)
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");

        note.IsFavorite = !note.IsFavorite;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);

        return ToDto(note);
    }
}
