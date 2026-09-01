using Brainy.Application.Caching;
using Brainy.Application.DTOs.Summaries;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="Summary"/> entities scoped to the current user's notes.
/// Reads use <c>AsNoTracking</c> for performance; writes load tracked entities.
/// </summary>
internal sealed class SummaryService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : ISummaryService
{
    public async Task<IReadOnlyList<SummaryDto>> GetByNoteAsync(
        Guid noteId,
        CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"summaries:note:{noteId}",
            [
                ApplicationCacheKey.EntityTypeTag<Summary>(),
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(noteId)
            ],
            async token => await context.Summaries
                .AsNoTracking()
                .Where(s => s.NoteId == noteId && s.Note.UserId == userId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .Select(s => new SummaryDto(
                    s.Id, s.NoteId, s.Content,
                    s.IsAiGenerated, s.Model, s.PromptVersion,
                    s.CreatedAtUtc))
                .ToListAsync(token).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    public async Task<SummaryDto> CreateAsync(
        CreateSummaryDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Content))
            throw new ArgumentException("Summary content must not be empty.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var note = await context.Notes
            .Where(n => n.Id == dto.NoteId && n.UserId == userId)
            .Select(n => new { n.Title })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (note is null)
            throw new KeyNotFoundException($"Note '{dto.NoteId}' was not found.");

        var summary = new Summary
        {
            Id            = Guid.NewGuid(),
            NoteId        = dto.NoteId,
            Content       = dto.Content.Trim(),
            IsAiGenerated = dto.IsAiGenerated,
            Model         = dto.Model,
            PromptVersion = dto.PromptVersion
        };

        var activity = new LifecycleActivity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EntityId = summary.Id,
            ActivityType = PulseActivityType.SummaryCreated,
            OccurredAtUtc = DateTime.UtcNow,
            Title = note.Title,
            Context = summary.IsAiGenerated ? "AI summary" : "Summary added",
            Link = $"/notes/{dto.NoteId}",
        };
        context.Summaries.Add(summary);
        context.LifecycleActivities.Add(activity);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Summary>(),
                ApplicationCacheKey.EntityTag<Summary>(summary.Id),
                ApplicationCacheKey.EntityTypeTag<LifecycleActivity>(),
                ApplicationCacheKey.EntityTag<LifecycleActivity>(activity.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);

        return new SummaryDto(
            summary.Id,
            summary.NoteId,
            summary.Content,
            summary.IsAiGenerated,
            summary.Model,
            summary.PromptVersion,
            summary.CreatedAtUtc);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var summary = await context.Summaries
            .FirstOrDefaultAsync(s => s.Id == id && s.Note.UserId == userId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Summary '{id}' was not found.");

        context.Summaries.Remove(summary);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Summary>(),
                ApplicationCacheKey.EntityTag<Summary>(summary.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }
}
