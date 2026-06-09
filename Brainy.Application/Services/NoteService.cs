using Brainy.Application.DTOs.Notes;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
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

    private static NoteDto ToDto(Note n) => new(
        n.Id,
        n.Title,
        n.Content,
        n.AiSummary,
        n.Status,
        n.ParaCategory,
        n.SourceId,
        n.ProjectId,
        n.AreaId,
        n.ResourceId,
        n.CreatedAtUtc,
        n.UpdatedAtUtc);
}
