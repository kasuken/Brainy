using Brainy.Application.Common;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Recommends the single best task to focus on when the user has no Current Task designated.
/// Candidates are scored by urgency (overdue/due-today) combined with task priority,
/// then the highest-scored task is returned.
/// </summary>
internal sealed class CurrentTaskRecommendationService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone) : ICurrentTaskRecommendationService
{
    private static readonly TaskItemStatus[] _excludedStatuses =
        [TaskItemStatus.Done, TaskItemStatus.Archived];

    public async Task<TodayTaskItemDto?> GetRecommendationAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var weekEnd = today.AddDays(7);

        // Fetch all eligible candidates in one query; scoring is done in memory
        // because the multi-factor score expression cannot be cleanly translated to SQL.
        var candidates = await context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsArchived
                        && t.Project.Status == ProjectStatus.Active
                        && !t.Project.IsArchived
                        && t.ParentTaskId == null
                        && !_excludedStatuses.Contains(t.Status))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Description,
                t.Status,
                t.Priority,
                t.DueDate,
                t.ProjectId,
                ProjectName = t.Project.Name,
                t.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return null;

        var best = candidates
            .Select(t =>
            {
                var isOverdue = t.DueDate.HasValue && t.DueDate.Value.Date < today;
                var isDueToday = t.DueDate.HasValue && t.DueDate.Value.Date == today;
                var isDueThisWeek = t.DueDate.HasValue
                                    && t.DueDate.Value.Date > today
                                    && t.DueDate.Value.Date <= weekEnd;

                // Score buckets mirror the product spec for recommendation priority.
                var score = (isOverdue, isDueToday, t.Priority) switch
                {
                    (true, _, TaskPriority.Critical)  => 100,
                    (true, _, TaskPriority.High)       => 80,
                    (_, true, TaskPriority.Critical)   => 70,
                    (_, true, TaskPriority.High)       => 60,
                    (true, _, TaskPriority.Medium)     => 50,
                    (_, true, TaskPriority.Medium)     => 40,
                    (_, _, TaskPriority.Critical)      => 30,
                    _ when isDueThisWeek && t.Priority == TaskPriority.High => 20,
                    _                                  => 10,
                };

                return (Score: score, Task: t);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Task.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(x => x.Task.Priority)
            .First();

        return new TodayTaskItemDto(
            best.Task.Id,
            best.Task.Title,
            best.Task.Description,
            best.Task.Status,
            best.Task.Priority,
            best.Task.DueDate,
            best.Task.ProjectId,
            best.Task.ProjectName,
            best.Task.CreatedAtUtc);
    }
}
