using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Common;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Area"/> entities, scoped to the current user.
/// Active areas exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class AreaService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IAreaService
{
    public async Task<IReadOnlyList<AreaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await cache.GetOrCreateAsync(
            userId,
            "areas:active",
            [ApplicationCacheKey.EntityTypeTag<Area>()],
            async ct => await context.Areas.AsNoTracking()
                .Where(a => a.UserId == userId && !a.IsArchived)
                .OrderBy(a => a.Name)
                .Select(a => ToDto(a))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AreaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await cache.GetOrCreateAsync(
            userId,
            "areas:archived",
            [ApplicationCacheKey.EntityTypeTag<Area>()],
            async ct => await context.Areas.AsNoTracking()
                .Where(a => a.UserId == userId && a.IsArchived)
                .OrderByDescending(a => a.ArchivedAtUtc)
                .Select(a => ToDto(a))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AreaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await cache.GetOrCreateAsync(
            userId,
            $"areas:{id}:summary",
            [
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTag<Area>(id)
            ],
            async ct =>
            {
                var area = await context.Areas.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct).ConfigureAwait(false);
                return area is null ? null : ToDto(area);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AreaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"areas:{id}:detail",
            [
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTag<Area>(id),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Note>()
            ],
            async ct =>
            {
                var area = await context.Areas.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct).ConfigureAwait(false);

                if (area is null) return null;

                // These tiles sit directly above the lists that render the same records, so every
                // filter here must mirror the list queries. A count that includes rows the sections
                // below exclude renders as a populated summary above empty sections.
                var activeProjectCount = await context.Projects.AsNoTracking()
                    .CountAsync(p => p.AreaId == id && p.UserId == userId
                                  && !p.IsArchived && p.Status != ProjectStatus.Archived, ct)
                    .ConfigureAwait(false);

                // Top-level tasks only: subtasks render nested under their parent rather than as
                // standalone rows, so counting them here would overstate the outstanding work.
                var openTaskCount = await context.Tasks.AsNoTracking()
                    .CountAsync(t => t.Project.AreaId == id && t.UserId == userId
                                  && t.ParentTaskId == null
                                  && !t.IsArchived
                                  && !t.Project.IsArchived && t.Project.Status != ProjectStatus.Archived
                                  && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Archived, ct)
                    .ConfigureAwait(false);

                var recentNoteCount = await context.Notes.AsNoTracking()
                    .CountAsync(n => n.AreaId == id && n.UserId == userId
                                  && !n.IsArchived && n.Status != NoteStatus.Archived, ct)
                    .ConfigureAwait(false);

                return new AreaDetailDto(area.Id, area.Name, area.Description, area.Purpose,
                    area.IsArchived, area.ArchivedAtUtc, area.CreatedAtUtc, area.UpdatedAtUtc,
                    activeProjectCount, openTaskCount, recentNoteCount,
                    NormalizeEmoji(area.Emoji), area.ArchivedReason);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AreaDto> CreateAsync(CreateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Area name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = new Area
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Trim(),
            Emoji = NormalizeEmoji(dto.Emoji),
            Description = dto.Description?.Trim(),
            Purpose = dto.Purpose?.Trim()
        };

        context.Areas.Add(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAreaAsync(userId, area.Id).ConfigureAwait(false);
        return ToDto(area);
    }

    public async Task<AreaDto> UpdateAsync(UpdateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Area name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == dto.Id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{dto.Id}' was not found.");

        area.Name = dto.Name.Trim();
        area.Emoji = NormalizeEmoji(dto.Emoji);
        area.Description = dto.Description?.Trim();
        area.Purpose = dto.Purpose?.Trim();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAreaAsync(userId, area.Id).ConfigureAwait(false);
        return ToDto(area);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        if (area.IsArchived)
            return;

        var blockers = new List<string>();
        AddBlocker("project", await context.Projects.AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.AreaId == id && !p.IsArchived && p.Status != ProjectStatus.Archived, cancellationToken)
            .ConfigureAwait(false));
        AddBlocker("resource", await context.Resources.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.AreaId == id && !r.IsArchived, cancellationToken)
            .ConfigureAwait(false));
        AddBlocker("goal", await context.Goals.AsNoTracking()
            .CountAsync(g => g.UserId == userId && g.AreaId == id && !g.IsArchived && g.Status != GoalStatus.Archived, cancellationToken)
            .ConfigureAwait(false));
        AddBlocker("note", await context.Notes.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.AreaId == id && !n.IsArchived && n.Status != NoteStatus.Archived, cancellationToken)
            .ConfigureAwait(false));
        AddBlocker("idea", await context.Ideas.AsNoTracking()
            .CountAsync(i => i.UserId == userId && i.AreaId == id && !i.IsArchived, cancellationToken)
            .ConfigureAwait(false));
        AddBlocker("output", await context.Outputs.AsNoTracking()
            .CountAsync(o => o.UserId == userId && o.AreaId == id && !o.IsArchived && o.Status != OutputStatus.Archived, cancellationToken)
            .ConfigureAwait(false));

        if (blockers.Count > 0)
            throw new InvalidOperationException(
                $"Area '{area.Name}' cannot be archived while it contains {string.Join(", ", blockers)}. " +
                "Archive or reassign those items first.");

        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        area.IsArchived = true;
        area.ArchivedAtUtc = DateTime.UtcNow;
        area.ArchivedReason = normalizedReason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAreaAsync(userId, area.Id).ConfigureAwait(false);

        return;

        void AddBlocker(string label, int count)
        {
            if (count > 0)
                blockers.Add($"{count} active {label}{(count == 1 ? string.Empty : "s")}");
        }
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        area.IsArchived = false;
        area.ArchivedAtUtc = null;
        area.ArchivedReason = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateAreaAsync(userId, area.Id).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        var hasActiveProjects = await context.Projects
            .AnyAsync(p => p.AreaId == id && !p.IsArchived, cancellationToken).ConfigureAwait(false);
        if (hasActiveProjects)
            throw new InvalidOperationException(
                "This area cannot be deleted because it has active projects. " +
                "Archive or reassign all projects before deleting the area.");

        context.Areas.Remove(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTag<Area>(area.Id),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTypeTag<Idea>(),
                ApplicationCacheKey.EntityTypeTag<Output>()
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task LinkProjectAsync(Guid areaId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var areaExists = await context.Areas
            .AnyAsync(a => a.Id == areaId && a.UserId == userId && !a.IsArchived, cancellationToken).ConfigureAwait(false);
        if (!areaExists)
            throw new KeyNotFoundException($"Area '{areaId}' was not found.");

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        project.AreaId = areaId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTag<Project>(project.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public Task UnlinkProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "A project must always be related to an area. " +
            "Use LinkProjectAsync to move the project to a different area instead.");

    public async Task LinkNoteAsync(Guid areaId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var areaExists = await context.Areas
            .AnyAsync(a => a.Id == areaId && a.UserId == userId && !a.IsArchived, cancellationToken).ConfigureAwait(false);
        if (!areaExists)
            throw new KeyNotFoundException($"Area '{areaId}' was not found.");

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.AreaId = areaId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(note.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task UnlinkNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.AreaId = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(note.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    private ValueTask InvalidateAreaAsync(string userId, Guid areaId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTag<Area>(areaId)
            ],
            CancellationToken.None);

    private static AreaDto ToDto(Area a) => new(
        a.Id, a.Name, a.Description, a.Purpose,
        a.IsArchived, a.ArchivedAtUtc,
        a.CreatedAtUtc, a.UpdatedAtUtc,
        NormalizeEmoji(a.Emoji), a.ArchivedReason);

    private static string NormalizeEmoji(string? emoji)
    {
        var normalized = string.IsNullOrWhiteSpace(emoji)
            ? AreaEmojiDefaults.DefaultEmoji
            : emoji.Trim();

        if (normalized.Length > AreaEmojiDefaults.MaxLength)
            throw new ArgumentException($"Area emoji cannot exceed {AreaEmojiDefaults.MaxLength} characters.", nameof(emoji));

        return normalized;
    }
}
