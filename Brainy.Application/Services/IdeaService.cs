using Brainy.Application.Common;
using Brainy.Application.DTOs.Ideas;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD, archiving, review, and commit-to-project operations for
/// <see cref="Idea"/> entities, scoped to the current user.
/// Active ideas exclude archived entries; reads use <c>AsNoTracking</c>.
/// An idea may only move to <see cref="IdeaStatus.Committed"/> via <see cref="CommitToProjectAsync"/>,
/// which validates the five commitment criteria before creating the linked project.
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
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IdeaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.IsArchived)
            .OrderByDescending(i => i.ArchivedAtUtc)
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IdeaDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureOwnedAsync(areaId, userId, "Area", cancellationToken)
            .ConfigureAwait(false);

        return await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.AreaId == areaId && !i.IsArchived)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IdeaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas.AsNoTracking()
            .Include(i => i.Area)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false);

        return idea is null ? null : ToDto(idea, idea.Area?.UserId == userId ? idea.Area.Name : null);
    }

    public async Task<IdeaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas.AsNoTracking()
            .Include(i => i.Area)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false);

        return idea is null ? null : ToDetailDto(idea, idea.Area?.UserId == userId ? idea.Area.Name : null);
    }

    public async Task<IdeaReviewDto> GetReviewDataAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        // Stale: not updated in 30+ days; still actionable (not rejected/committed/shipped).
        var staleThreshold = now.AddDays(-30);
        var stale = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId
                     && !i.IsArchived
                     && i.Status != IdeaStatus.Rejected
                     && i.Status != IdeaStatus.Committed
                     && i.Status != IdeaStatus.Shipped
                     && i.UpdatedAtUtc < staleThreshold)
            .OrderBy(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Recently updated: touched in last 7 days.
        var recentThreshold = now.AddDays(-7);
        var recent = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && !i.IsArchived && i.UpdatedAtUtc >= recentThreshold)
            .OrderByDescending(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // High/Critical priority with no activity in 14+ days; still actionable.
        var activityThreshold = now.AddDays(-14);
        var highPriority = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId
                     && !i.IsArchived
                     && i.Status != IdeaStatus.Rejected
                     && i.Status != IdeaStatus.Committed
                     && i.Status != IdeaStatus.Shipped
                     && (i.Priority == IdeaPriority.High || i.Priority == IdeaPriority.Critical)
                     && i.UpdatedAtUtc < activityThreshold)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.UpdatedAtUtc)
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new IdeaReviewDto(stale, recent, highPriority);
    }

    public async Task<IdeaMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var total     = await context.Ideas.CountAsync(i => i.UserId == userId, cancellationToken).ConfigureAwait(false);
        var active    = await context.Ideas.CountAsync(i => i.UserId == userId && !i.IsArchived, cancellationToken).ConfigureAwait(false);
        var archived  = await context.Ideas.CountAsync(i => i.UserId == userId && i.IsArchived, cancellationToken).ConfigureAwait(false);
        var committed = await context.Ideas.CountAsync(i => i.UserId == userId && i.Status == IdeaStatus.Committed, cancellationToken).ConfigureAwait(false);
        var rejected  = await context.Ideas.CountAsync(i => i.UserId == userId && i.Status == IdeaStatus.Rejected, cancellationToken).ConfigureAwait(false);
        var shipped   = await context.Ideas.CountAsync(i => i.UserId == userId && i.Status == IdeaStatus.Shipped, cancellationToken).ConfigureAwait(false);

        var byArea = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId)
            .GroupBy(i => new { i.AreaId, AreaName = i.Area != null && i.Area.UserId == userId ? i.Area.Name : null })
            .Select(g => new IdeasByAreaDto(
                g.Key.AreaId,
                g.Key.AreaName ?? "No Area",
                g.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new IdeaMetricsDto(total, active, archived, committed, rejected, shipped, byArea);
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
            .Select(i => ToDto(i, i.Area != null && i.Area.UserId == userId ? i.Area.Name : null))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public async Task<IdeaDto> CreateAsync(CreateIdeaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Idea title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

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
                .Where(a => a.Id == idea.AreaId.Value && a.UserId == userId)
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

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (dto.RowVersion is not null)
            context.Entry(idea).Property(i => i.RowVersion).OriginalValue = dto.RowVersion;

        if (dto.Status == IdeaStatus.Committed && idea.Status != IdeaStatus.Committed)
            throw new InvalidOperationException(
                "An idea can only become Committed through CommitToProjectAsync, which validates the commitment criteria and creates the project.");

        idea.Title                = dto.Title.Trim();
        idea.Description          = dto.Description?.Trim();
        idea.AreaId               = dto.AreaId;
        idea.Priority             = dto.Priority;
        idea.Status               = dto.Status;
        idea.Research             = dto.Research?.Trim();
        idea.Competitors          = dto.Competitors?.Trim();
        idea.Notes                = dto.Notes?.Trim();
        idea.TargetUserAndProblem = dto.TargetUserAndProblem?.Trim();
        idea.SuitabilityReason    = dto.SuitabilityReason?.Trim();
        idea.Evidence             = dto.Evidence?.Trim();
        idea.ValidationExperiment = dto.ValidationExperiment?.Trim();
        idea.ReplacedCommitment   = dto.ReplacedCommitment?.Trim();

        // A no-op form submission must still validate the token captured by the editor.
        context.Entry(idea).Property(i => i.UpdatedAtUtc).IsModified = true;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("idea", ex);
        }

        string? areaName = null;
        if (idea.AreaId.HasValue)
        {
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == idea.AreaId.Value && a.UserId == userId)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(idea, areaName);
    }

    /// <summary>Soft-archives the idea. Sets IsArchived = true, ArchivedAtUtc = UtcNow. Status is left unchanged.</summary>
    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        idea.IsArchived    = true;
        idea.ArchivedAtUtc = DateTime.UtcNow;
        idea.ArchivedReason = normalizedReason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restores an archived idea. Clears IsArchived and ArchivedAtUtc. Status is left unchanged.</summary>
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(idea.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        idea.IsArchived    = false;
        idea.ArchivedAtUtc = null;
        idea.ArchivedReason = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        Guid id,
        byte[]? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{id}' was not found.");

        if (rowVersion is not null)
            context.Entry(idea).Property(i => i.RowVersion).OriginalValue = rowVersion;

        context.Ideas.Remove(idea);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("idea", ex);
        }
    }

    public async Task<IdeaDto> CommitToProjectAsync(
        CommitIdeaToProjectDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var idea = await context.Ideas
            .FirstOrDefaultAsync(i => i.Id == dto.Id && i.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Idea '{dto.Id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(idea.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (idea.Status == IdeaStatus.Committed)
            throw new InvalidOperationException($"Idea '{dto.Id}' has already been committed.");

        var targetUserAndProblem = dto.TargetUserAndProblem?.Trim();
        var suitabilityReason = dto.SuitabilityReason?.Trim();
        var evidence = dto.Evidence?.Trim();
        var validationExperiment = dto.ValidationExperiment?.Trim();
        var replacedCommitment = dto.ReplacedCommitment?.Trim();

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(targetUserAndProblem)) missing.Add("a specific user and problem");
        if (string.IsNullOrWhiteSpace(suitabilityReason)) missing.Add("a reason you are suited to build or write it");
        if (string.IsNullOrWhiteSpace(evidence)) missing.Add("one piece of real evidence");
        if (string.IsNullOrWhiteSpace(validationExperiment)) missing.Add("a small validation experiment");
        if (string.IsNullOrWhiteSpace(replacedCommitment)) missing.Add("what existing commitment it will replace");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Idea '{dto.Id}' cannot be committed yet. Missing: {string.Join("; ", missing)}.");

        if (dto.RowVersion is not null)
            context.Entry(idea).Property(i => i.RowVersion).OriginalValue = dto.RowVersion;

        idea.TargetUserAndProblem = targetUserAndProblem;
        idea.SuitabilityReason = suitabilityReason;
        idea.Evidence = evidence;
        idea.ValidationExperiment = validationExperiment;
        idea.ReplacedCommitment = replacedCommitment;

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

        idea.Status             = IdeaStatus.Committed;
        idea.CommittedProjectId = project.Id;
        idea.CommittedAtUtc     = DateTime.UtcNow;

        // Move bulky content to the project; the idea keeps only a link and its decision record.
        idea.Description = null;
        idea.Research     = null;
        idea.Competitors  = null;
        idea.Notes        = null;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            context.Entry(project).State = EntityState.Detached;
            context.Entry(idea).State = EntityState.Detached;
            throw new ConcurrencyConflictException("idea", ex);
        }

        string? areaName = null;
        if (idea.AreaId.HasValue)
        {
            areaName = await context.Areas.AsNoTracking()
                .Where(a => a.Id == idea.AreaId.Value && a.UserId == userId)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return ToDto(idea, areaName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IdeaDto ToDto(Idea i, string? areaName) => new(
        i.Id, i.Title, i.Description, i.AreaId, areaName,
        i.Priority, i.Status, i.IsArchived, i.ArchivedAtUtc,
        i.CreatedAtUtc, i.UpdatedAtUtc, i.CommittedProjectId, i.RowVersion, i.ArchivedReason);

    private static IdeaDetailDto ToDetailDto(Idea i, string? areaName) => new(
        i.Id, i.Title, i.Description, i.AreaId, areaName,
        i.Priority, i.Status, i.IsArchived, i.ArchivedAtUtc,
        i.CreatedAtUtc, i.UpdatedAtUtc,
        i.Research, i.Competitors, i.Notes,
        i.TargetUserAndProblem, i.SuitabilityReason, i.Evidence, i.ValidationExperiment, i.ReplacedCommitment,
        i.CommittedProjectId, i.CommittedAtUtc, i.RowVersion, i.ArchivedReason);
}
