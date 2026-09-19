using Brainy.Application.Caching;
using Brainy.Application.DTOs.Today;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Detects contradictions between a project's status and the status of its tasks
/// (e.g. in-progress work in a project that is not Active, an archived project that
/// still has open tasks, or an Active project whose work is all done). Each project
/// yields at most one warning so the Today screen stays quiet unless something is
/// genuinely out of step.
/// </summary>
internal sealed class StatusConsistencyService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IStatusConsistencyService
{
    public async Task<IReadOnlyList<StatusConsistencyWarningDto>> GetWarningsAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "today:status-consistency",
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>()
            ],
            ct => BuildWarningsAsync(userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<StatusConsistencyWarningDto>> BuildWarningsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        // Roll every project's task statuses up into a small per-project snapshot in a
        // single query. Only non-archived tasks are counted (an archived task is a
        // closed one), and "open" means not Done and not the Archived status.
        var snapshots = await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new ProjectStatusSnapshot
            {
                ProjectId = p.Id,
                Name = p.Name,
                Status = p.Status,
                IsArchived = p.IsArchived,
                TaskCount = p.Tasks.Count(t => !t.IsArchived && t.Status != TaskItemStatus.Archived),
                InProgressCount = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.InProgress),
                OpenCount = p.Tasks.Count(t => !t.IsArchived
                                               && t.Status != TaskItemStatus.Done
                                               && t.Status != TaskItemStatus.Archived),
                DoneCount = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.Done)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var warnings = new List<StatusConsistencyWarningDto>();

        foreach (var s in snapshots)
        {
            var warning = Evaluate(s);
            if (warning is not null)
                warnings.Add(warning);
        }

        // Most important first (Warning before Info, then by kind), then alphabetically
        // so the list is stable between refreshes.
        return warnings
            .OrderByDescending(w => w.Severity)
            .ThenBy(w => w.Kind)
            .ThenBy(w => w.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the single most relevant inconsistency for a project, or null when its
    /// status and tasks are in step. The branches are mutually exclusive by project
    /// status, so a project never produces more than one warning.
    /// </summary>
    private static StatusConsistencyWarningDto? Evaluate(ProjectStatusSnapshot s)
    {
        var isArchived = s.IsArchived || s.Status == ProjectStatus.Archived;

        // An archived project should have nothing left to do.
        if (isArchived)
        {
            return s.OpenCount > 0
                ? Warn(StatusConsistencyKind.ArchivedProjectWithOpenTasks, StatusConsistencySeverity.Warning, s,
                    $"“{s.Name}” is archived but still has {Tasks(s.OpenCount)} open. Close them or restore the project.")
                : null;
        }

        switch (s.Status)
        {
            case ProjectStatus.Completed:
                // Finished project, unfinished work left inside it.
                return s.OpenCount > 0
                    ? Warn(StatusConsistencyKind.CompletedProjectWithOpenTasks, StatusConsistencySeverity.Warning, s,
                        $"“{s.Name}” is marked Completed but still has {Tasks(s.OpenCount)} unfinished. Reopen the project or close the tasks.")
                    : null;

            case ProjectStatus.Active:
                // Active project whose every task is already done — likely ready to complete.
                return s.TaskCount > 0 && s.OpenCount == 0
                    ? Warn(StatusConsistencyKind.ActiveProjectAllTasksDone, StatusConsistencySeverity.Info, s,
                        $"“{s.Name}” is Active but all {Tasks(s.DoneCount)} are done. Ready to mark it Completed?")
                    : null;

            case ProjectStatus.NotStarted:
                // Work is underway or finished, yet the project was never activated.
                if (s.InProgressCount > 0)
                    return Warn(StatusConsistencyKind.InProgressWorkInInactiveProject, StatusConsistencySeverity.Warning, s,
                        $"“{s.Name}” has {Tasks(s.InProgressCount)} in progress but the project is still Not Started. Activate it or pause the work.");
                return s.DoneCount > 0
                    ? Warn(StatusConsistencyKind.NotStartedProjectWithProgress, StatusConsistencySeverity.Info, s,
                        $"“{s.Name}” already has completed work but is still marked Not Started. Set it to Active.")
                    : null;

            case ProjectStatus.Blocked:
            case ProjectStatus.Parked:
                // You can't actively work a project that's blocked or parked.
                return s.InProgressCount > 0
                    ? Warn(StatusConsistencyKind.InProgressWorkInInactiveProject, StatusConsistencySeverity.Warning, s,
                        $"“{s.Name}” has {Tasks(s.InProgressCount)} in progress but the project is {StatusLabel(s.Status)}. Reactivate it or pause the work.")
                    : null;

            default:
                return null;
        }
    }

    private static StatusConsistencyWarningDto Warn(
        StatusConsistencyKind kind,
        StatusConsistencySeverity severity,
        ProjectStatusSnapshot s,
        string message) =>
        new(kind, severity, s.ProjectId, s.Name, s.Status,
            kind == StatusConsistencyKind.InProgressWorkInInactiveProject ? s.InProgressCount
            : kind == StatusConsistencyKind.ActiveProjectAllTasksDone || kind == StatusConsistencyKind.NotStartedProjectWithProgress ? s.DoneCount
            : s.OpenCount,
            message);

    private static string Tasks(int count) => count == 1 ? "1 task" : $"{count} tasks";

    private static string StatusLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.NotStarted => "Not Started",
        ProjectStatus.Active => "Active",
        ProjectStatus.Blocked => "Blocked",
        ProjectStatus.Parked => "Parked",
        ProjectStatus.Completed => "Completed",
        ProjectStatus.Archived => "Archived",
        _ => status.ToString()
    };

    private sealed class ProjectStatusSnapshot
    {
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = string.Empty;
        public ProjectStatus Status { get; init; }
        public bool IsArchived { get; init; }

        /// <summary>Non-archived tasks that are not in the Archived status.</summary>
        public int TaskCount { get; init; }

        /// <summary>Non-archived tasks in the In Progress status.</summary>
        public int InProgressCount { get; init; }

        /// <summary>Non-archived tasks that are neither Done nor Archived.</summary>
        public int OpenCount { get; init; }

        /// <summary>Non-archived tasks in the Done status.</summary>
        public int DoneCount { get; init; }
    }
}
