using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Note"/> entities.
/// Reads use <c>AsNoTracking</c> for performance; writes load tracked entities.
/// </summary>
internal sealed class NoteService(IApplicationDbContext context) : INoteService
{
    public async Task<IReadOnlyList<NoteDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.Notes
            .AsNoTracking()
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => ToDto(n))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var note = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return note is null ? null : ToDto(note);
    }

    public async Task<NoteDto> CreateAsync(CreateNoteDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var note = new Note
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Content = dto.Content,
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

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == dto.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{dto.Id}' was not found.");

        note.Title = dto.Title;
        note.Content = dto.Content;
        note.Status = dto.Status;
        note.ParaCategory = dto.ParaCategory;
        note.ProjectId = dto.ProjectId;
        note.AreaId = dto.AreaId;
        note.ResourceId = dto.ResourceId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(note);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{id}' was not found.");

        context.Notes.Remove(note);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NoteDto ToDto(Note n) => new(
        n.Id,
        n.Title,
        n.Content,
        n.Status,
        n.ParaCategory,
        n.SourceId,
        n.ProjectId,
        n.AreaId,
        n.ResourceId,
        n.CreatedAtUtc,
        n.UpdatedAtUtc);
}
