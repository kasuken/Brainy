using Brainy.Application.DTOs.Highlights;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="Highlight"/> entities scoped to the current user's notes.
/// Reads use <c>AsNoTracking</c> for performance; writes load tracked entities.
/// </summary>
internal sealed class HighlightService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : IHighlightService
{
    public async Task<IReadOnlyList<HighlightDto>> GetByNoteAsync(
        Guid noteId,
        CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        return await context.Highlights
            .AsNoTracking()
            .Where(h => h.NoteId == noteId && h.Note.UserId == userId)
            .OrderBy(h => h.Layer)
            .ThenBy(h => h.CreatedAtUtc)
            .Select(h => new HighlightDto(h.Id, h.NoteId, h.Text, h.Annotation, h.Layer, h.CreatedAtUtc))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<HighlightDto> CreateAsync(
        CreateHighlightDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Text))
            throw new ArgumentException("Highlight text must not be empty.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var noteExists = await context.Notes
            .AnyAsync(n => n.Id == dto.NoteId && n.UserId == userId, ct)
            .ConfigureAwait(false);

        if (!noteExists)
            throw new KeyNotFoundException($"Note '{dto.NoteId}' was not found.");

        var highlight = new Highlight
        {
            Id         = Guid.NewGuid(),
            NoteId     = dto.NoteId,
            Text       = dto.Text.Trim(),
            Annotation = string.IsNullOrWhiteSpace(dto.Annotation) ? null : dto.Annotation.Trim(),
            Layer      = dto.Layer
        };

        context.Highlights.Add(highlight);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        return new HighlightDto(
            highlight.Id,
            highlight.NoteId,
            highlight.Text,
            highlight.Annotation,
            highlight.Layer,
            highlight.CreatedAtUtc);
    }

    public async Task UpdateAsync(
        Guid id,
        UpdateHighlightDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var highlight = await context.Highlights
            .FirstOrDefaultAsync(h => h.Id == id && h.Note.UserId == userId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Highlight '{id}' was not found.");

        highlight.Annotation = string.IsNullOrWhiteSpace(dto.Annotation) ? null : dto.Annotation.Trim();
        highlight.Layer      = dto.Layer;

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var highlight = await context.Highlights
            .FirstOrDefaultAsync(h => h.Id == id && h.Note.UserId == userId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Highlight '{id}' was not found.");

        context.Highlights.Remove(highlight);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
