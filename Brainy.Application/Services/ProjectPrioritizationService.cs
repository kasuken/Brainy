using Brainy.Application.Common;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Common;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Scores active projects by priority, task urgency, and deadline proximity,
/// returning them ranked highest-first for display on the Today screen.
/// </summary>
internal sealed class ProjectPrioritizationService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IProjectPrioritizationService
{
    private static readonly TaskItemStatus[] _inactiveStatuses =
        [TaskItemStatus.Done, TaskItemStatus.Archived];

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetPrioritizedProjectsAsync(
        int maxCount = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = timeProvider.GetUserToday();
        var weekEnd = today.AddDays(7);

        // Fetch active projects with their task statistics in one query.
        var data = await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsArchived && p.Status == ProjectStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.DesiredOutcome, p.Status, p.Priority,
                p.StartDate, p.DueDate, p.CompletedDate, p.IsArchived, p.AreaId,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.ArchivedAtUtc,
                p.Emoji,
                TotalTasks = p.Tasks.Count(t => !t.IsArchived),
                OpenTasks = p.Tasks.Count(t => !t.IsArchived && !_inactiveStatuses.Contains(t.Status)),
                DoneTasks = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.Done),
                OverdueTasks = p.Tasks.Count(t => !t.IsArchived
                                                  && !_inactiveStatuses.Contains(t.Status)
                                                  && t.DueDate.HasValue
                                                  && t.DueDate.Value.Date < today),
                HasDueToday = p.Tasks.Any(t => !t.IsArchived
                                               && !_inactiveStatuses.Contains(t.Status)
                                               && t.DueDate.HasValue
                                               && t.DueDate.Value.Date == today),
                HasDueThisWeek = p.Tasks.Any(t => !t.IsArchived
                                                  && !_inactiveStatuses.Contains(t.Status)
                                                  && t.DueDate.HasValue
                                                  && t.DueDate.Value.Date > today
                                                  && t.DueDate.Value.Date <= weekEnd),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Score and rank in memory — avoids translating complex switch logic to SQL.
        return data
            .Select(x =>
            {
                var baseScore = x.Priority switch
                {
                    ProjectPriority.Critical => 40,
                    ProjectPriority.High     => 30,
                    ProjectPriority.Medium   => 20,
                    _                        => 10,
                };

                var score = baseScore
                    + (x.OverdueTasks > 0 ? 20 : 0)
                    + (x.HasDueToday ? 10 : 0)
                    + (x.HasDueThisWeek ? 5 : 0);

                return (Score: score, Data: x);
            })
            .OrderByDescending(x => x.Score)
            // Tie-break: earliest DueDate first; projects without a due date go last.
            .ThenBy(x => x.Data.DueDate.HasValue ? x.Data.DueDate : DateTime.MaxValue)
            .Take(maxCount)
            .Select(x => new ProjectSummaryDto(
                x.Data.Id,
                x.Data.Name,
                x.Data.Description,
                x.Data.DesiredOutcome,
                x.Data.Status,
                x.Data.Priority,
                x.Data.StartDate,
                x.Data.DueDate,
                x.Data.CompletedDate,
                x.Data.IsArchived,
                x.Data.AreaId,
                x.Data.CreatedAtUtc,
                x.Data.UpdatedAtUtc,
                x.Data.ArchivedAtUtc,
                x.Data.TotalTasks,
                x.Data.OpenTasks,
                x.Data.DoneTasks,
                x.Data.TotalTasks > 0
                    ? Math.Round((double)x.Data.DoneTasks / x.Data.TotalTasks * 100, 1)
                    : 0.0,
                x.Data.OverdueTasks,
                string.IsNullOrWhiteSpace(x.Data.Emoji) ? ProjectEmojiDefaults.DefaultEmoji : x.Data.Emoji))
            .ToList();
    }
}
