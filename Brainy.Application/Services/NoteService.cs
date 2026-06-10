using Brainy.Application.DTOs.Notes;
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
internal sealed class NoteService(IApplicationDbContext context, ICurrentUserService currentUser) : INoteService
{
    public async Task<IReadOnlyList<NoteDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return note is null ? null : ToDto(note);
    }

    public async Task<NoteDto> CreateAsync(CreateNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

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

        context.Notes.Add(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(note);
    }

    public async Task<NoteDto> UpdateAsync(UpdateNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == dto.Id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.Id}' was not found.");

        note.Title = dto.Title;
        note.Content = dto.Content;
        note.AiSummary = dto.AiSummary;
        note.Status = dto.Status;
        note.ParaCategory = dto.ParaCategory;
        note.ProjectId = dto.ProjectId;
        note.AreaId = dto.AreaId;
        note.ResourceId = dto.ResourceId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(note);
    }

    public async Task<IReadOnlyList<NoteDto>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.Status == NoteStatus.Inbox)
            .OrderBy(n => n.CreatedAtUtc)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteDto> ProcessNoteAsync(ProcessNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == dto.Id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.Id}' was not found.");

        note.Status          = dto.Status;
        note.ParaCategory    = dto.ParaCategory;
        note.ProjectId       = dto.ProjectId;
        note.AreaId          = dto.AreaId;
        note.ResourceId      = dto.ResourceId;
        note.ProcessedAtUtc  = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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

        return await context.Notes
            .Where(n => n.UserId == userId && idList.Contains(n.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.ParaCategory, category)
                    .SetProperty(n => n.Status, status)
                    .SetProperty(n => n.ProjectId, projectId)
                    .SetProperty(n => n.AreaId, areaId)
                    .SetProperty(n => n.ResourceId, resourceId)
                    .SetProperty(n => n.ProcessedAtUtc, DateTime.UtcNow)
                    .SetProperty(n => n.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> BulkMoveCategoryAsync(IEnumerable<Guid> ids, ParaCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids as ICollection<Guid> ?? ids.ToList();
        if (idList.Count == 0) return 0;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Notes
            .Where(n => n.UserId == userId && idList.Contains(n.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.ParaCategory, category)
                    .SetProperty(n => n.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NoteDto>> GetNotLinkedToProjectAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ProjectId == null && n.Status != NoteStatus.Archived)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteDto> LinkToProjectAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ProjectId = projectId;

        // Only upgrade category if it's not already categorised as a project item
        if (note.ParaCategory != ParaCategory.Project && note.ParaCategory != ParaCategory.Resource)
            note.ParaCategory = ParaCategory.Project;

        // Promote inbox notes to Active
        if (note.Status == NoteStatus.Inbox)
            note.Status = NoteStatus.Active;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task<NoteDto> UnlinkFromProjectAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ProjectId = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(note);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");

        context.Notes.Remove(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NoteDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.IsArchived)
            .OrderByDescending(n => n.ArchivedAtUtc)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");
        note.IsArchived = true;
        note.ArchivedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");
        note.IsArchived = false;
        note.ArchivedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        n.UpdatedAtUtc);
}
