using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Goals;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="Goal"/> entities scoped to the current user.
/// All reads use <c>AsNoTracking</c>; all operations are async.
/// </summary>
internal sealed class GoalService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache) : IGoalService
{
    public async Task<IReadOnlyList<GoalDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "goals:active",
            GoalReadTags(),
            async ct =>
            {
                var goals = await GoalQuery(userId)
                    .Where(g => !g.IsArchived && g.Status != GoalStatus.Abandoned && g.Status != GoalStatus.Archived)
                    .OrderBy(g => g.TargetDate)
                    .ThenBy(g => g.Title)
                    .ToListAsync(ct).ConfigureAwait(false);
                return goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "goals:non-archived",
            GoalReadTags(),
            async ct =>
            {
                var goals = await GoalQuery(userId)
                    .Where(g => !g.IsArchived && g.Status != GoalStatus.Archived)
                    .OrderBy(g => g.Status)
                    .ThenBy(g => g.TargetDate)
                    .ThenBy(g => g.Title)
                    .ToListAsync(ct).ConfigureAwait(false);
                return goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "goals:archived",
            GoalReadTags(),
            async ct =>
            {
                var goals = await GoalQuery(userId)
                    .Where(g => g.IsArchived || g.Status == GoalStatus.Archived)
                    .OrderByDescending(g => g.ArchivedAtUtc)
                    .ThenBy(g => g.Title)
                    .ToListAsync(ct).ConfigureAwait(false);
                return goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:area:{areaId}",
            GoalReadTags(),
            async ct =>
            {
                var goals = await GoalQuery(userId)
                    .Where(g => g.AreaId == areaId && !g.IsArchived && g.Status != GoalStatus.Archived)
                    .OrderBy(g => g.TargetDate)
                    .ThenBy(g => g.Title)
                    .ToListAsync(ct).ConfigureAwait(false);
                return goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoalDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:{id}:detail",
            GoalReadTags(id, includeProjects: true),
            async ct =>
            {
                var goal = await GoalQuery(userId)
                    .Include(g => g.Projects)
                    .FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);

                if (goal is null) return null;

                var milestones = goal.Milestones
                    .OrderBy(m => m.CreatedAtUtc)
                    .Select(ToMilestoneDto)
                    .ToList();
                var projects = goal.Projects
                    .Where(p => !p.IsArchived && p.Status != ProjectStatus.Archived)
                    .Select(p => new LinkedProjectDto(p.Id, p.Name, p.Status))
                    .ToList();
                var total = milestones.Count;
                var completed = milestones.Count(m => m.IsCompleted);
                var progress = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total);

                return new GoalDetailDto(
                    goal.Id, goal.Title, goal.Description, goal.AreaId, goal.Area?.Name,
                    goal.Status, goal.TargetDate, goal.AchievedDate, goal.IsArchived,
                    goal.ArchivedAtUtc, total, completed, progress, goal.CreatedAtUtc,
                    goal.UpdatedAtUtc, milestones, projects, goal.RowVersion, goal.ArchivedReason);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoalDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:{id}:summary",
            GoalReadTags(id),
            async ct =>
            {
                var goal = await GoalQuery(userId)
                    .FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
                return goal is null ? null : ToDto(goal);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoalDto> CreateAsync(CreateGoalDto dto, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        var goal = new Goal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = dto.Title,
            Description = dto.Description,
            AreaId = dto.AreaId,
            TargetDate = dto.TargetDate,
            Status = GoalStatus.Planned
        };

        context.Goals.Add(goal);
        var activity = new GoalActivity
        {
            Id           = Guid.NewGuid(),
            GoalId       = goal.Id,
            ActivityType = GoalActivityType.Created,
            Description  = "Goal created",
        };
        context.GoalActivities.Add(activity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateGoalAsync(userId, goal.Id, [activity.Id]).ConfigureAwait(false);

        return await GetByIdAsync(goal.Id, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to retrieve newly created goal.");
    }

    public async Task<GoalDto> UpdateAsync(UpdateGoalDto dto, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var goal = await context.Goals
            .Where(g => g.Id == dto.Id && g.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Goal {dto.Id} not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (dto.RowVersion is not null)
            context.Entry(goal).Property(g => g.RowVersion).OriginalValue = dto.RowVersion;

        // Capture old values before mutation for activity recording
        var oldTitle       = goal.Title;
        var oldDescription = goal.Description;
        var oldStatus      = goal.Status;
        var oldTargetDate  = goal.TargetDate;

        goal.Title = dto.Title;
        goal.Description = dto.Description;
        goal.AreaId = dto.AreaId;
        goal.TargetDate = dto.TargetDate;
        goal.Status = dto.Status;

        if (dto.Status == GoalStatus.Achieved && goal.AchievedDate is null)
            goal.AchievedDate = DateTime.UtcNow;
        else if (dto.Status != GoalStatus.Achieved)
            goal.AchievedDate = null;

        // A no-op form submission must still validate the token captured by the editor.
        context.Entry(goal).Property(g => g.UpdatedAtUtc).IsModified = true;

        // Record one activity per changed field
        var activities = new List<GoalActivity>();

        if (!string.Equals(oldTitle, dto.Title, StringComparison.Ordinal))
            activities.Add(Activity(goal.Id, GoalActivityType.TitleEdited, "Title updated", oldTitle, dto.Title));

        if (!string.Equals(oldDescription, dto.Description, StringComparison.Ordinal))
            activities.Add(Activity(goal.Id, GoalActivityType.DescriptionEdited, "Description updated", oldDescription, dto.Description));

        if (oldStatus != dto.Status)
            activities.Add(Activity(goal.Id, GoalActivityType.StatusChanged,
                $"Status changed from {oldStatus} to {dto.Status}", oldStatus.ToString(), dto.Status.ToString()));

        if (oldTargetDate != dto.TargetDate)
            activities.Add(Activity(goal.Id, GoalActivityType.TargetDateChanged, "Target date changed",
                oldTargetDate?.ToString("O"), dto.TargetDate?.ToString("O")));

        if (activities.Count > 0)
            context.GoalActivities.AddRange(activities);

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("goal", ex);
        }

        await InvalidateGoalAsync(
            userId,
            goal.Id,
            activities.Select(activity => activity.Id)).ConfigureAwait(false);
        return await GetByIdAsync(goal.Id, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to retrieve updated goal.");
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var goal = await context.Goals
            .Where(g => g.Id == id && g.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Goal {id} not found.");

        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        goal.IsArchived = true;
        goal.ArchivedAtUtc = DateTime.UtcNow;
        goal.ArchivedReason = normalizedReason;
        goal.Status = GoalStatus.Archived;

        var activity = new GoalActivity
        {
            Id           = Guid.NewGuid(),
            GoalId       = id,
            ActivityType = GoalActivityType.Archived,
            Description  = "Goal archived",
        };
        context.GoalActivities.Add(activity);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateGoalAsync(userId, goal.Id, [activity.Id]).ConfigureAwait(false);
    }

    public async Task<GoalDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var goal = await context.Goals
            .Where(g => g.Id == id && g.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Goal {id} not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(goal.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        goal.IsArchived = false;
        goal.ArchivedAtUtc = null;
        goal.ArchivedReason = null;
        goal.Status = GoalStatus.Planned;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateGoalAsync(userId, goal.Id).ConfigureAwait(false);

        return await GetByIdAsync(goal.Id, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Failed to retrieve restored goal.");
    }

    public async Task DeleteAsync(
        Guid id,
        byte[]? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var goal = await context.Goals
            .Where(g => g.Id == id && g.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Goal {id} not found.");

        if (rowVersion is not null)
            context.Entry(goal).Property(g => g.RowVersion).OriginalValue = rowVersion;

        context.Goals.Remove(goal);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("goal", ex);
        }
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTag<Goal>(goal.Id),
                ApplicationCacheKey.EntityTypeTag<GoalActivity>(),
                ApplicationCacheKey.EntityTypeTag<GoalMilestone>(),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<Output>()
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<int> GetProgressAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:{id}:progress",
            [
                ApplicationCacheKey.EntityTypeTag<GoalMilestone>(),
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTag<Goal>(id)
            ],
            async ct =>
            {
                var counts = await context.GoalMilestones
                    .AsNoTracking()
                    .Where(m => m.GoalId == id && m.Goal!.UserId == userId)
                    .GroupBy(_ => 1)
                    .Select(g => new { Total = g.Count(), Completed = g.Count(m => m.IsCompleted) })
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                return counts is null || counts.Total == 0
                    ? 0
                    : (int)Math.Round(counts.Completed * 100.0 / counts.Total);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoalMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "goals:metrics",
            GoalReadTags(),
            async ct =>
            {
                var goals = await GoalQuery(userId).ToListAsync(ct).ConfigureAwait(false);
                var active = goals.Count(g => !g.IsArchived && g.Status == GoalStatus.Active);
                var achieved = goals.Count(g => g.Status == GoalStatus.Achieved);
                var abandoned = goals.Count(g => g.Status == GoalStatus.Abandoned);
                var byArea = goals
                    .Where(g => !g.IsArchived && g.Status != GoalStatus.Archived && g.AreaId.HasValue && g.Area is not null)
                    .GroupBy(g => new { g.AreaId, AreaName = g.Area!.Name })
                    .Select(grp => new AreaGoalCountDto(grp.Key.AreaId!.Value, grp.Key.AreaName, grp.Count()))
                    .OrderByDescending(x => x.GoalCount)
                    .ToList();
                var activeGoals = goals.Where(g => !g.IsArchived && g.Status == GoalStatus.Active).ToList();
                var avgRate = activeGoals.Count == 0
                    ? 0.0
                    : activeGoals.Average(g =>
                    {
                        var total = g.Milestones.Count;
                        var completed = g.Milestones.Count(m => m.IsCompleted);
                        return total == 0 ? 0.0 : completed * 100.0 / total;
                    });
                return new GoalMetricsDto(active, achieved, abandoned, byArea, Math.Round(avgRate, 1));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalDto>> GetDueSoonAsync(int daysAhead = 7, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = today.AddDays(daysAhead);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:due-soon:{today:yyyy-MM-dd}:{daysAhead}",
            [.. GoalReadTags(), ApplicationCacheKey.TimeZoneTag],
            async ct =>
            {
                var goals = await context.Goals
                    .AsNoTracking()
                    .Include(g => g.Area)
                    .Include(g => g.Milestones)
                    .Where(g => g.UserId == userId
                                && !g.IsArchived
                                && g.Status != GoalStatus.Achieved
                                && g.Status != GoalStatus.Archived
                                && g.TargetDate.HasValue
                                && g.TargetDate.Value.Date >= today
                                && g.TargetDate.Value.Date <= cutoff)
                    .OrderBy(g => g.TargetDate)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                return (IReadOnlyList<GoalDto>)goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoalDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:overdue:{today:yyyy-MM-dd}",
            [.. GoalReadTags(), ApplicationCacheKey.TimeZoneTag],
            async ct =>
            {
                var goals = await context.Goals
                    .AsNoTracking()
                    .Include(g => g.Area)
                    .Include(g => g.Milestones)
                    .Where(g => g.UserId == userId
                                && !g.IsArchived
                                && g.Status != GoalStatus.Achieved
                                && g.Status != GoalStatus.Archived
                                && g.TargetDate.HasValue
                                && g.TargetDate.Value.Date < today)
                    .OrderBy(g => g.TargetDate)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                return (IReadOnlyList<GoalDto>)goals.Select(ToDto).ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static GoalDto ToDto(Goal g)
    {
        var total = g.Milestones.Count;
        var completed = g.Milestones.Count(m => m.IsCompleted);
        var progress = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total);

        return new GoalDto(
            g.Id,
            g.Title,
            g.Description,
            g.AreaId,
            g.Area?.Name,
            g.Status,
            g.TargetDate,
            g.AchievedDate,
            g.IsArchived,
            g.ArchivedAtUtc,
            total,
            completed,
            progress,
            g.CreatedAtUtc,
            g.UpdatedAtUtc,
            g.RowVersion,
            g.ArchivedReason
        );
    }

    private static GoalMilestoneDto ToMilestoneDto(GoalMilestone m) =>
        new(m.Id, m.GoalId, m.Title, m.IsCompleted, m.CompletedAtUtc, m.CreatedAtUtc);

    public async Task<IReadOnlyList<GoalActivityDto>> GetActivitiesAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goals:{goalId}:activities",
            [
                ApplicationCacheKey.EntityTypeTag<GoalActivity>(),
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTag<Goal>(goalId)
            ],
            async ct => await context.GoalActivities
                .AsNoTracking()
                .Where(a => a.GoalId == goalId && a.Goal.UserId == userId)
                .OrderBy(a => a.CreatedAtUtc)
                .Select(a => new GoalActivityDto(a.Id, a.ActivityType, a.Description, a.OldValue, a.NewValue, a.CreatedAtUtc))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private static GoalActivity Activity(
        Guid goalId,
        GoalActivityType type,
        string description,
        string? oldValue = null,
        string? newValue = null) =>
        new()
        {
            Id           = Guid.NewGuid(),
            GoalId       = goalId,
            ActivityType = type,
            Description  = description,
            OldValue     = oldValue,
            NewValue     = newValue,
        };

    private static IReadOnlyCollection<string> GoalReadTags(
        Guid? goalId = null,
        bool includeProjects = false)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Goal>(),
            ApplicationCacheKey.EntityTypeTag<Area>(),
            ApplicationCacheKey.EntityTypeTag<GoalMilestone>()
        ];
        if (goalId.HasValue)
            tags.Add(ApplicationCacheKey.EntityTag<Goal>(goalId.Value));
        if (includeProjects)
            tags.Add(ApplicationCacheKey.EntityTypeTag<Project>());
        return tags;
    }

    private ValueTask InvalidateGoalAsync(
        string userId,
        Guid goalId,
        IEnumerable<Guid>? activityIds = null)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Goal>(),
            ApplicationCacheKey.EntityTag<Goal>(goalId),
            ApplicationCacheKey.EntityTypeTag<LifecycleActivity>()
        ];
        if (activityIds is not null)
        {
            var ids = activityIds.ToList();
            if (ids.Count > 0)
            {
                tags.Add(ApplicationCacheKey.EntityTypeTag<GoalActivity>());
                tags.AddRange(ids.Select(ApplicationCacheKey.EntityTag<GoalActivity>));
            }
        }

        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private IQueryable<Goal> GoalQuery(string userId) =>
        context.Goals
            .AsNoTracking()
            .Include(g => g.Area)
            .Include(g => g.Milestones)
            .Where(g => g.UserId == userId);
}
