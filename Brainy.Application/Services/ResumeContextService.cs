using Brainy.Application.Analytics;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Builds the Resume Context read model (issue #305) entirely from data the task's owner
/// already has: the task's own subtasks and dependencies, the most recently touched note
/// or output in its project, and the project's/goal's due dates. Deliberately has no
/// dependency on any AI service — "next actionable step" is derived, never inferred.
/// </summary>
internal sealed class ResumeContextService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache,
    IAnalyticsService analytics) : IResumeContextService
{
    private const int RestartNoteMaxLength = 2000;

    public async Task<ResumeContextDto?> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await BuildAsync(taskId, userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResumeContextDto> SaveRestartNoteAsync(
        Guid taskId,
        string? restartNote,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        var trimmed = restartNote?.Trim();
        task.RestartNote = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length > RestartNoteMaxLength ? trimmed[..RestartNoteMaxLength] : trimmed;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await cache.InvalidateTagsAsync(
            userId,
            [ApplicationCacheKey.EntityTypeTag<TaskItem>(), ApplicationCacheKey.EntityTag<TaskItem>(taskId)],
            cancellationToken).ConfigureAwait(false);

        // Volume/usage signal only — never the note text itself, per issue #305's "Measure" section.
        await analytics.TrackAsync(userId, AnalyticsEvents.ResumeContextRestartNoteSaved, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = await BuildAsync(taskId, userId, cancellationToken).ConfigureAwait(false);
        return result ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");
    }

    private async Task<ResumeContextDto?> BuildAsync(Guid taskId, string userId, CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .AsNoTracking()
            .Include(t => t.Project)
                .ThenInclude(p => p.Goal)
            .Include(t => t.Subtasks.Where(s => !s.IsArchived))
            .Include(t => t.Dependencies)
                .ThenInclude(d => d.DependsOnTask)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null)
            return null;

        // Mirrors the "blocked" definition used everywhere else current-focus eligibility
        // is decided (SetCurrentTaskAsync, CurrentTaskRecommendationService, FocusPickerDialog):
        // an incomplete prerequisite, not the manual "Waiting" status.
        var unresolvedDependencyTitle = task.Dependencies
            .Where(d => d.DependsOnTask.Status != TaskItemStatus.Done)
            .OrderBy(d => d.DependsOnTask.DueDate ?? DateTime.MaxValue)
            .ThenBy(d => d.DependsOnTask.Title)
            .Select(d => d.DependsOnTask.Title)
            .FirstOrDefault();

        var nextSubtaskTitle = task.Subtasks
            .Where(s => s.Status != TaskItemStatus.Done)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.CreatedAtUtc)
            .Select(s => s.Title)
            .FirstOrDefault();

        var linkedItem = await GetMostRecentLinkedItemAsync(userId, task.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        return new ResumeContextDto(
            TaskId: task.Id,
            TaskStatus: task.Status,
            IsArchived: task.IsArchived,
            IsBlocked: unresolvedDependencyTitle is not null,
            UnresolvedDependencyTitle: unresolvedDependencyTitle,
            HasSubtasks: task.Subtasks.Count > 0,
            NextSubtaskTitle: nextSubtaskTitle,
            MostRecentLinkedItem: linkedItem,
            ProjectId: task.ProjectId,
            ProjectName: task.Project.Name,
            ProjectDueDate: task.Project.DueDate,
            GoalId: task.Project.GoalId,
            GoalTitle: task.Project.Goal?.Title,
            GoalDueDate: task.Project.Goal?.TargetDate,
            RestartNote: task.RestartNote);
    }

    /// <summary>
    /// "Most recently linked note, source, output or activity" (issue #305) has no direct
    /// note-to-task or output-to-task relationship in the current data model — see
    /// <see cref="Note"/> and <see cref="Output"/>, both of which only link to a project.
    /// The most defensible, already-derivable signal is therefore the most recently
    /// touched note or output within the task's own project, scoped strictly to the
    /// current user and excluding archived records.
    /// </summary>
    private async Task<ResumeContextLinkedItemDto?> GetMostRecentLinkedItemAsync(
        string userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var recentNote = await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ProjectId == projectId && !n.IsArchived)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new { n.Id, n.Title, n.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var recentOutput = await context.Outputs
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.ProjectId == projectId && !o.IsArchived)
            .OrderByDescending(o => o.UpdatedAtUtc)
            .Select(o => new { o.Id, o.Title, o.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (recentNote is null && recentOutput is null)
            return null;

        if (recentOutput is null)
            return new ResumeContextLinkedItemDto(recentNote!.Id, recentNote.Title, ResumeContextLinkedItemType.Note, recentNote.UpdatedAtUtc);

        if (recentNote is null)
            return new ResumeContextLinkedItemDto(recentOutput.Id, recentOutput.Title, ResumeContextLinkedItemType.Output, recentOutput.UpdatedAtUtc);

        return recentNote.UpdatedAtUtc >= recentOutput.UpdatedAtUtc
            ? new ResumeContextLinkedItemDto(recentNote.Id, recentNote.Title, ResumeContextLinkedItemType.Note, recentNote.UpdatedAtUtc)
            : new ResumeContextLinkedItemDto(recentOutput.Id, recentOutput.Title, ResumeContextLinkedItemType.Output, recentOutput.UpdatedAtUtc);
    }
}
