using Brainy.Application.DTOs.Ideas;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD, archiving, review, and project-conversion operations for
/// <see cref="Idea"/> entities, scoped to the current user.
/// Active ideas exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class IdeaService(IApplicationDbContext context, ICurrentUserService currentUser) : IIdeaService
{
    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<IdeaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IdeaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.IsArchived)
            .OrderByDescending(i => i.ArchivedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IdeaDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.AreaId == areaId && !i.IsArchived)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IdeaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas.AsNoTracking()
            .Include(i => i.Area)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false);

        return idea is null ? null : ToDto(idea, idea.Area?.Name);
    }

    public async Task<IdeaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas.AsNoTracking()
            .Include(i => i.Area)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false);

        return idea is null ? null : ToDetailDto(idea, idea.Area?.Name);
    }

    public async Task<IdeaReviewDto> GetReviewDataAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        // Stale: not updated in 30+ days; still actionable (not archived/rejected/converted).
        var staleThreshold = now.AddDays(-30);
        var stale = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId
                     && !i.IsArchived
                     && i.Status != IdeaStatus.Archived
                     && i.Status != IdeaStatus.Rejected
                     && i.Status != IdeaStatus.ConvertedToProject
                     && i.UpdatedAtUtc < staleThreshold)
            .OrderBy(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Recently updated: touched in last 7 days.
        var recentThreshold = now.AddDays(-7);
        var recent = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived && i.UpdatedAtUtc >= recentThreshold)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // High/Critical priority with no activity in 14+ days; still actionable.
        var activityThreshold = now.AddDays(-14);
        var highPriority = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId
                     && !i.IsArchived
                     && i.Status != IdeaStatus.Archived
                     && i.Status != IdeaStatus.Rejected
                     && i.Status != IdeaStatus.ConvertedToProject
                     && (i.Priority == IdeaPriority.High || i.Priority == IdeaPriority.Critical)
                     && i.UpdatedAtUtc < activityThreshold)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new IdeaReviewDto(stale, recent, highPriority);
    }

    public async Task<IdeaMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var total     = await context.Ideas.CountAsync(i => i.UserId == userId, cancellationToken).ConfigureAwait(false);
        var active    = await context.Ideas.CountAsync(i => i.UserId == userId && !i.IsArchived, cancellationToken).ConfigureAwait(false);
        var archived  = await context.Ideas.CountAsync(i => i.UserId == userId && i.IsArchived, cancellationToken).ConfigureAwait(false);
        var converted = await context.Ideas.CountAsync(i => i.UserId == userId && i.Status == IdeaStatus.ConvertedToProject, cancellationToken).ConfigureAwait(false);
        var rejected  = await context.Ideas.CountAsync(i => i.UserId == userId && i.Status == IdeaStatus.Rejected, cancellationToken).ConfigureAwait(false);

        var byArea = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId)
            .GroupBy(i => new { i.AreaId, AreaName = i.Area != null ? i.Area.Name : null })
            .Select(g => new IdeasByAreaDto(
                g.Key.AreaId,
                g.Key.AreaName ?? "No Area",
                g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new IdeaMetricsDto(total, active, archived, converted, rejected, byArea);
    }

    public async Task<IReadOnlyList<IdeaDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var term = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
            return await GetAllActiveAsync(cancellationToken).ConfigureAwait(false);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived &&
                        (i.Title.Contains(term) ||
                         (i.Description != null && i.Description.Contains(term)) ||
                         (i.Research != null && i.Research.Contains(term)) ||
                         (i.Competitors != null && i.Competitors.Contains(term)) ||
                         (i.Notes != null && i.Notes.Contains(term))))
            .OrderByDescending(i => i.Title.Contains(term) ? 1 : 0)
            .ThenByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public async Task<IdeaDto> CreateAsync(CreateIdeaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Idea title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = new Idea
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            Title       = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            AreaId      = dto.AreaId,
            Priority    = dto.Priority,
            Status      = IdeaStatus.Captured
        };

        context.Ideas.Add(idea);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Load the area name for the returned DTO.
        string? areaName = null;
        if (idea.AreaId.HasValue)
        {
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == idea.AreaId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(idea, areaName);
    }

    public async Task<IdeaDto> UpdateAsync(UpdateIdeaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Idea title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == dto.Id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{dto.Id}' was not found.");

        idea.Title       = dto.Title.Trim();
        idea.Description = dto.Description?.Trim();
        idea.AreaId      = dto.AreaId;
        idea.Priority    = dto.Priority;
        idea.Status      = dto.Status;
        idea.Research    = dto.Research?.Trim();
        idea.Competitors = dto.Competitors?.Trim();
        idea.Notes       = dto.Notes?.Trim();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? areaName = null;
        if (idea.AreaId.HasValue)
        {
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == idea.AreaId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(idea, areaName);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        idea.IsArchived    = true;
        idea.ArchivedAtUtc = DateTime.UtcNow;
        idea.Status        = IdeaStatus.Archived;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        idea.IsArchived    = false;
        idea.ArchivedAtUtc = null;
        idea.Status        = IdeaStatus.Captured;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        context.Ideas.Remove(idea);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IdeaDto> ConvertToProjectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        if (idea.Status == IdeaStatus.ConvertedToProject)
            throw new InvalidOperationException($"Idea '{id}' has already been converted to a project.");

        var project = new Project
        {
            Id          = Guid.NewGuid(),
            UserId      = idea.UserId,
            Name        = idea.Title,
            Description = idea.Description,
            AreaId      = idea.AreaId,
            Status      = ProjectStatus.NotStarted,
            Priority    = ProjectPriority.Medium
        };

        context.Projects.Add(project);

        idea.Status = IdeaStatus.ConvertedToProject;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? areaName = null;
        if (idea.AreaId.HasValue)
        {
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == idea.AreaId.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(idea, areaName);
    }

    /// <inheritdoc/>
    public async Task<Guid> ConvertToNoteAsync(Guid ideaId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == ideaId && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{ideaId}' was not found.");

        var note = new Note
        {
            Id             = Guid.NewGuid(),
            UserId         = userId,
            Title          = idea.Title,
            Content        = idea.Description ?? string.Empty,
            ParaCategory   = ParaCategory.Resource,
            Status         = NoteStatus.Active,
            ProcessedAtUtc = DateTime.UtcNow
        };

        context.Notes.Add(note);
        idea.Status = IdeaStatus.ConvertedToNote;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return note.Id;
    }

    /// <inheritdoc/>
    public async Task<Guid> ConvertToTaskAsync(Guid ideaId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == ideaId && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{ideaId}' was not found.");

        var projectExists = await context.Projects
            .AnyAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken).ConfigureAwait(false);

        if (!projectExists)
            throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var task = new TaskItem
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            ProjectId   = projectId,
            Title       = idea.Title,
            Description = idea.Description,
            Priority    = TaskPriority.Medium,
            Status      = TaskItemStatus.Todo
        };

        context.Tasks.Add(task);
        idea.Status = IdeaStatus.ConvertedToTask;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return task.Id;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IdeaDto ToDto(Idea i, string? areaName) => new(
        i.Id, i.Title, i.Description, i.AreaId, areaName,
        i.Priority, i.Status, i.IsArchived, i.ArchivedAtUtc,
        i.CreatedAtUtc, i.UpdatedAtUtc);

    private static IdeaDetailDto ToDetailDto(Idea i, string? areaName) => new(
        i.Id, i.Title, i.Description, i.AreaId, areaName,
        i.Priority, i.Status, i.IsArchived, i.ArchivedAtUtc,
        i.CreatedAtUtc, i.UpdatedAtUtc,
        i.Research, i.Competitors, i.Notes);
}
