using Brainy.Application.Caching;
using Brainy.Application.DTOs.Highlights;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="Highlight"/> entities scoped to the current user's notes.
/// Reads use <c>AsNoTracking</c> for performance; writes load tracked entities.
/// </summary>
internal sealed class HighlightService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IHighlightService
{
    public async Task<IReadOnlyList<HighlightDto>> GetByNoteAsync(
        Guid noteId,
        CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"highlights:note:{noteId}",
            [
                ApplicationCacheKey.EntityTypeTag<Highlight>(),
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(noteId)
            ],
            async token => await context.Highlights
                .AsNoTracking()
                .Where(h => h.NoteId == noteId && h.Note.UserId == userId)
                .OrderBy(h => h.Layer)
                .ThenBy(h => h.CreatedAtUtc)
                .Select(h => new HighlightDto(h.Id, h.NoteId, h.Text, h.Annotation, h.Layer,
                    h.CreatedAtUtc, h.StartOffset, h.EndOffset))
                .ToListAsync(token).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    public async Task<HighlightDto> CreateAsync(
        CreateHighlightDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Text))
            throw new ArgumentException("Highlight text must not be empty.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var note = await context.Notes
            .Where(n => n.Id == dto.NoteId && n.UserId == userId)
            .Select(n => new { n.Content, n.Title })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (note is null)
            throw new KeyNotFoundException($"Note '{dto.NoteId}' was not found.");

        var text = dto.Text.Trim();
        var (startOffset, endOffset) = ResolveOffsets(note.Content, text, dto.StartOffset, dto.EndOffset);

        var highlight = new Highlight
        {
            Id         = Guid.NewGuid(),
            NoteId     = dto.NoteId,
            Text       = text,
            Annotation = string.IsNullOrWhiteSpace(dto.Annotation) ? null : dto.Annotation.Trim(),
            Layer      = dto.Layer,
            StartOffset = startOffset,
            EndOffset = endOffset
        };

        var activity = new LifecycleActivity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EntityId = highlight.Id,
            ActivityType = PulseActivityType.HighlightAdded,
            OccurredAtUtc = DateTime.UtcNow,
            Title = note.Title,
            Context = "Highlight added",
            Link = $"/notes/{dto.NoteId}",
        };
        context.Highlights.Add(highlight);
        context.LifecycleActivities.Add(activity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Highlight>(),
                ApplicationCacheKey.EntityTag<Highlight>(highlight.Id),
                ApplicationCacheKey.EntityTypeTag<LifecycleActivity>(),
                ApplicationCacheKey.EntityTag<LifecycleActivity>(activity.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);

        return new HighlightDto(
            highlight.Id,
            highlight.NoteId,
            highlight.Text,
            highlight.Annotation,
            highlight.Layer,
            highlight.CreatedAtUtc,
            highlight.StartOffset,
            highlight.EndOffset);
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
        await InvalidateHighlightAsync(userId, highlight.Id).ConfigureAwait(false);
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
        await InvalidateHighlightAsync(userId, highlight.Id).ConfigureAwait(false);
    }

    private ValueTask InvalidateHighlightAsync(string userId, Guid highlightId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Highlight>(),
                ApplicationCacheKey.EntityTag<Highlight>(highlightId)
            ],
            CancellationToken.None);

    private static (int? Start, int? End) ResolveOffsets(
        string noteContent,
        string highlightedText,
        int? requestedStart,
        int? requestedEnd)
    {
        if (requestedStart.HasValue || requestedEnd.HasValue)
        {
            if (!requestedStart.HasValue || !requestedEnd.HasValue
                || requestedStart.Value < 0
                || requestedEnd.Value <= requestedStart.Value
                || requestedEnd.Value > noteContent.Length
                || !noteContent.AsSpan(requestedStart.Value, requestedEnd.Value - requestedStart.Value)
                    .Equals(highlightedText.AsSpan(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Highlight offsets must identify the exact selected text.");
            }

            return (requestedStart, requestedEnd);
        }

        var inferredStart = noteContent.IndexOf(highlightedText, StringComparison.Ordinal);
        return inferredStart < 0
            ? (null, null)
            : (inferredStart, inferredStart + highlightedText.Length);
    }
}
