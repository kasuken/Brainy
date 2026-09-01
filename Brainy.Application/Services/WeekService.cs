using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Week;
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
/// Implements explicit Monday-Sunday weekly planning for the authenticated user.
/// </summary>
internal sealed class WeekService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache) : IWeekService
{
    private static readonly ProjectStatus[] OverviewStatuses =
        [ProjectStatus.NotStarted, ProjectStatus.Active, ProjectStatus.Blocked, ProjectStatus.Parked];

    /// <inheritdoc />
    public async Task<WeekOverviewDto> GetCurrentWeekOverviewAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        return await cache.GetOrCreateAsync(
            userId,
            ApplicationCacheKey.Create("week", "overview", week.WeekStartDate, today),
            WeekReadTags(),
            ct => GetCurrentWeekOverviewCoreAsync(userId, today, week, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WeekOverviewDto> GetCurrentWeekOverviewCoreAsync(
        string userId,
        DateTime today,
        WeekWindow week,
        CancellationToken cancellationToken)
    {
        var projects = (await BuildProjectOverviewQuery(userId, today, week.WeekStartDate, planningStatusesOnly: true)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(project => project.Priority)
            .ThenBy(project => project.Status)
            .ThenBy(project => project.DueDate)
            .ThenBy(project => project.Name)
            .Select(project => project.ToDto())
            .ToList();

        var selectedTasks = await ProjectTasks(
                context.WeeklyTaskSelections
                    .AsNoTracking()
                    .Where(selection => selection.UserId == userId && selection.WeekStartDate == week.WeekStartDate)
                    .Select(selection => selection.Task),
                today,
                week.WeekEndDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var selectedTaskIds = selectedTasks.Select(task => task.Id).ToHashSet();

        var projectCardsById = projects.ToDictionary(project => project.Id);
        var selectedProjectIds = selectedTasks.Select(task => task.ProjectId).Distinct().ToList();
        var missingProjectIds = selectedProjectIds.Where(projectId => !projectCardsById.ContainsKey(projectId)).ToList();
        if (missingProjectIds.Count > 0)
        {
            var missingProjects = await BuildProjectOverviewQuery(userId, today, week.WeekStartDate)
                .Where(project => missingProjectIds.Contains(project.Id))
                .Select(project => project.ToDto())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var project in missingProjects)
                projectCardsById[project.Id] = project;
        }

        var overdueAttention = (await ProjectTasks(
                ActiveTopLevelTasks(userId)
                    .Where(task =>
                        !selectedTaskIds.Contains(task.Id) &&
                        ((task.DueDate.HasValue && task.DueDate.Value.Date < today) ||
                         task.Subtasks.Any(subtask =>
                             !subtask.IsArchived &&
                             subtask.Status != TaskItemStatus.Done &&
                             subtask.Status != TaskItemStatus.Archived &&
                             subtask.DueDate.HasValue &&
                             subtask.DueDate.Value.Date < today))),
                today,
                week.WeekEndDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.Title)
            .ToList();

        var dueThisWeekAttention = (await ProjectTasks(
                ActiveTopLevelTasks(userId)
                    .Where(task =>
                        !selectedTaskIds.Contains(task.Id) &&
                        !((task.DueDate.HasValue && task.DueDate.Value.Date < today) ||
                          task.Subtasks.Any(subtask =>
                              !subtask.IsArchived &&
                              subtask.Status != TaskItemStatus.Done &&
                              subtask.Status != TaskItemStatus.Archived &&
                              subtask.DueDate.HasValue &&
                              subtask.DueDate.Value.Date < today)) &&
                        ((task.DueDate.HasValue && task.DueDate.Value.Date >= today && task.DueDate.Value.Date <= week.WeekEndDate) ||
                         task.Subtasks.Any(subtask =>
                             !subtask.IsArchived &&
                             subtask.Status != TaskItemStatus.Done &&
                             subtask.Status != TaskItemStatus.Archived &&
                             subtask.DueDate.HasValue &&
                             subtask.DueDate.Value.Date >= today &&
                             subtask.DueDate.Value.Date <= week.WeekEndDate))),
                today,
                week.WeekEndDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(task => task.DueDate)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.Title)
            .ToList();

        var selectedTaskCards = selectedTasks
            .Select(task => ToTaskCard(task, isSelectedForCurrentWeek: true))
            .ToList();

        var selectedGroups = selectedTaskCards
            .GroupBy(task => task.ProjectId)
            .Select(group =>
            {
                var project = projectCardsById[group.Key];
                return new WeekProjectPlanDto(
                    project,
                    group
                        .OrderBy(task => task.Status == TaskItemStatus.Done ? 1 : 0)
                        .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
                        .ThenByDescending(task => task.Priority)
                        .ThenBy(task => task.Title)
                        .ToList());
            })
            .OrderByDescending(group => group.Project.Priority)
            .ThenBy(group => group.Project.DueDate ?? DateTime.MaxValue)
            .ThenBy(group => group.Project.Name)
            .ToList();

        var needsReplanning = selectedTasks
            .Select(task => (Task: task, Reason: GetReplanningReason(task)))
            .Where(item => item.Task.Status != TaskItemStatus.Done && item.Reason is not null)
            .Select(item => ToTaskCard(item.Task, isSelectedForCurrentWeek: true, replanningReason: item.Reason))
            .OrderBy(task => task.ProjectName)
            .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
            .ThenBy(task => task.Title)
            .ToList();

        var carryForwardCandidates = await GetCarryForwardCandidatesCoreAsync(
                userId,
                today,
                week,
                selectedTaskIds,
                cancellationToken)
            .ConfigureAwait(false);

        return new WeekOverviewDto(
            today,
            week.WeekStartDate,
            week.WeekEndDate,
            week.WeekNumber,
            selectedTaskCards.Count,
            selectedTaskCards.Count(task => task.Status == TaskItemStatus.Done),
            selectedTaskCards.Count(task => task.Status != TaskItemStatus.Done),
            selectedTaskCards.Select(task => task.ProjectId).Distinct().Count(),
            projects,
            selectedGroups,
            overdueAttention.Select(task => ToTaskCard(
                task,
                attentionReason: task.DueDate.HasValue && task.DueDate.Value.Date < today
                    ? "Overdue."
                    : GetOverdueAttentionReason(task))).ToList(),
            dueThisWeekAttention.Select(task => ToTaskCard(
                task,
                attentionReason: task.DueDate.HasValue && task.DueDate.Value.Date >= today && task.DueDate.Value.Date <= week.WeekEndDate
                    ? "Due this week."
                    : GetDueThisWeekAttentionReason(task))).ToList(),
            needsReplanning,
            carryForwardCandidates);
    }

    /// <inheritdoc />
    public async Task<WeekTaskPickerDto> GetSelectableTasksAsync(
        Guid projectId,
        string? searchTerm = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults), "The picker limit must be greater than zero.");

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        return await cache.GetOrCreateAsync(
            userId,
            ApplicationCacheKey.Create(
                "week", "picker", week.WeekStartDate, today, projectId, searchTerm, maxResults),
            WeekReadTags(),
            ct => GetSelectableTasksCoreAsync(userId, today, week, projectId, searchTerm, maxResults, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WeekTaskPickerDto> GetSelectableTasksCoreAsync(
        string userId,
        DateTime today,
        WeekWindow week,
        Guid projectId,
        string? searchTerm,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects
            .AsNoTracking()
            .Where(candidate => candidate.Id == projectId && candidate.UserId == userId)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.Emoji, candidate.Status, candidate.IsArchived })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        if (project.IsArchived)
            throw new InvalidOperationException("Archived projects cannot be planned from the Week page.");

        var selectedTaskIds = await context.WeeklyTaskSelections
            .AsNoTracking()
            .Where(selection => selection.UserId == userId && selection.WeekStartDate == week.WeekStartDate)
            .Select(selection => selection.TaskId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tasksQuery = context.Tasks
            .AsNoTracking()
            .Where(task => task.UserId == userId
                           && task.ProjectId == projectId
                           && task.ParentTaskId == null
                           && !task.IsArchived);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var trimmed = searchTerm.Trim();
            tasksQuery = tasksQuery.Where(task =>
                task.Title.Contains(trimmed) ||
                (task.Description != null && task.Description.Contains(trimmed)));
        }

        var tasks = (await ProjectTasks(tasksQuery, today, week.WeekEndDate)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .OrderByDescending(task => selectedTaskIds.Contains(task.Id))
            .ThenBy(task => task.Status == TaskItemStatus.Done ? 1 : 0)
            .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(task => task.Priority)
            .ThenBy(task => task.Title)
            .Take(maxResults)
            .ToList();

        return new WeekTaskPickerDto(
            project.Id,
            project.Name,
            NormalizeEmoji(project.Emoji),
            project.Status,
            searchTerm,
            tasks.Select(task => ToTaskCard(task, selectedTaskIds.Contains(task.Id))).ToList());
    }

    /// <inheritdoc />
    public async Task AddTaskToCurrentWeekAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        var task = await ProjectTasks(
                context.Tasks
                    .AsNoTracking()
                    .Where(candidate => candidate.Id == taskId && candidate.UserId == userId),
                today,
                week.WeekEndDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        var existingSelection = await context.WeeklyTaskSelections
            .AsNoTracking()
            .AnyAsync(selection =>
                    selection.UserId == userId &&
                    selection.WeekStartDate == week.WeekStartDate &&
                    selection.TaskId == taskId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingSelection)
            return;

        var blockReason = GetSelectionBlockReason(task);
        if (blockReason is not null)
            throw new InvalidOperationException(blockReason);

        var selection = new WeeklyTaskSelection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TaskId = taskId,
            WeekStartDate = week.WeekStartDate
        };
        context.WeeklyTaskSelections.Add(selection);

        await SaveWeeklySelectionChangesAsync(selection, userId, taskId, week.WeekStartDate, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveTaskFromCurrentWeekAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        var ownedTaskExists = await context.Tasks
            .AsNoTracking()
            .AnyAsync(task => task.Id == taskId && task.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!ownedTaskExists)
            throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        var selections = await context.WeeklyTaskSelections
            .Where(selection =>
                selection.UserId == userId &&
                selection.WeekStartDate == week.WeekStartDate &&
                selection.TaskId == taskId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (selections.Count == 0)
            return;

        context.WeeklyTaskSelections.RemoveRange(selections);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateWeeklySelectionsAsync(
            userId,
            selections.Select(selection => selection.Id)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WeekCarryForwardCandidateDto>> GetCarryForwardCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        return await cache.GetOrCreateAsync(
            userId,
            ApplicationCacheKey.Create("week", "carry-forward", week.WeekStartDate, today),
            WeekReadTags(),
            ct => GetCarryForwardCandidatesCachedCoreAsync(userId, today, week, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WeekCarryForwardCandidateDto>> GetCarryForwardCandidatesCachedCoreAsync(
        string userId,
        DateTime today,
        WeekWindow week,
        CancellationToken cancellationToken)
    {
        var selectedTaskIds = await context.WeeklyTaskSelections
            .AsNoTracking()
            .Where(selection => selection.UserId == userId && selection.WeekStartDate == week.WeekStartDate)
            .Select(selection => selection.TaskId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        return await GetCarryForwardCandidatesCoreAsync(
                userId,
                today,
                week,
                selectedTaskIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CarryForwardTasksAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskIds);

        var normalizedTaskIds = taskIds.Distinct().ToList();
        if (normalizedTaskIds.Count == 0)
            return;

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        var previousWeekTasks = (await ProjectTasks(
                context.WeeklyTaskSelections
                    .AsNoTracking()
                    .Where(selection =>
                        selection.UserId == userId &&
                        selection.WeekStartDate == week.PreviousWeekStartDate &&
                        normalizedTaskIds.Contains(selection.TaskId) &&
                        selection.Task.Status != TaskItemStatus.Done)
                    .Select(selection => selection.Task),
                today,
                week.WeekEndDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

        if (previousWeekTasks.Count != normalizedTaskIds.Count)
            throw new KeyNotFoundException("One or more previous-week task selections were not found.");

        var currentWeekTaskIds = await context.WeeklyTaskSelections
            .AsNoTracking()
            .Where(selection => selection.UserId == userId && selection.WeekStartDate == week.WeekStartDate)
            .Select(selection => selection.TaskId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var task in previousWeekTasks)
        {
            if (currentWeekTaskIds.Contains(task.Id))
                continue;

            var blockReason = GetSelectionBlockReason(task);
            if (blockReason is not null)
                throw new InvalidOperationException(blockReason);

            var selection = new WeeklyTaskSelection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TaskId = task.Id,
                WeekStartDate = week.WeekStartDate
            };
            context.WeeklyTaskSelections.Add(selection);

            await SaveWeeklySelectionChangesAsync(selection, userId, task.Id, week.WeekStartDate, cancellationToken).ConfigureAwait(false);
            currentWeekTaskIds.Add(task.Id);
        }
    }

    /// <inheritdoc />
    public async Task<WeekProjectOverviewDto> UpdateProjectStatusAsync(
        WeekProjectStatusUpdateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Status is ProjectStatus.Completed or ProjectStatus.Archived)
            throw new InvalidOperationException("Week status changes only support Not Started, Active, Blocked, or Parked.");

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        var project = await context.Projects
            .FirstOrDefaultAsync(candidate => candidate.Id == dto.ProjectId && candidate.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{dto.ProjectId}' was not found.");

        if (project.IsArchived || project.Status is ProjectStatus.Completed or ProjectStatus.Archived)
            throw new InvalidOperationException("Completed or archived projects must be changed through their dedicated lifecycle workflows.");

        if (dto.RowVersion is not null)
            context.Entry(project).Property(candidate => candidate.RowVersion).OriginalValue = dto.RowVersion;

        project.Status = dto.Status;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("project", ex);
        }
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTag<Project>(project.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);

        return await BuildProjectOverviewQuery(userId, today, week.WeekStartDate)
            .Where(candidate => candidate.Id == dto.ProjectId)
            .Select(candidate => candidate.ToDto())
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WeekCarryForwardCandidateDto>> GetCarryForwardCandidatesCoreAsync(
        string userId,
        DateTime today,
        WeekWindow week,
        IReadOnlySet<Guid> selectedTaskIds,
        CancellationToken cancellationToken)
    {
        var previousWeekTasks = (await ProjectTasks(
                context.WeeklyTaskSelections
                    .AsNoTracking()
                    .Where(selection =>
                        selection.UserId == userId &&
                        selection.WeekStartDate == week.PreviousWeekStartDate &&
                        selection.Task.Status != TaskItemStatus.Done)
                    .Select(selection => selection.Task),
                today,
                week.WeekEndDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(task => task.ProjectName)
            .ThenBy(task => task.DueDate ?? DateTime.MaxValue)
            .ThenBy(task => task.Title)
            .ToList();

        return previousWeekTasks
            .Select(task =>
            {
                var blockReason = GetSelectionBlockReason(task);
                var alreadySelected = selectedTaskIds.Contains(task.Id);
                var carryForwardReason = alreadySelected
                    ? "Already selected for this week."
                    : blockReason;

                return new WeekCarryForwardCandidateDto(
                    ToTaskCard(task, isSelectedForCurrentWeek: alreadySelected),
                    week.PreviousWeekStartDate,
                    !alreadySelected && blockReason is null,
                    alreadySelected,
                    carryForwardReason);
            })
            .ToList();
    }

    private async Task SaveWeeklySelectionChangesAsync(
        WeeklyTaskSelection selection,
        string userId,
        Guid taskId,
        DateTime weekStartDate,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateWeeklySelectionsAsync(userId, [selection.Id]).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var exists = await context.WeeklyTaskSelections
                .AsNoTracking()
                .AnyAsync(selection =>
                        selection.UserId == userId &&
                        selection.TaskId == taskId &&
                        selection.WeekStartDate == weekStartDate,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
                throw;

            context.Entry(selection).State = EntityState.Detached;
        }
    }

    private static IReadOnlyCollection<string> WeekReadTags() =>
    [
        ApplicationCacheKey.EntityTypeTag<TaskItem>(),
        ApplicationCacheKey.EntityTypeTag<Project>(),
        ApplicationCacheKey.EntityTypeTag<WeeklyTaskSelection>(),
        ApplicationCacheKey.EntityTypeTag<TaskDependency>(),
        ApplicationCacheKey.TimeZoneTag
    ];

    private ValueTask InvalidateWeeklySelectionsAsync(
        string userId,
        IEnumerable<Guid> selectionIds)
    {
        List<string> tags = [ApplicationCacheKey.EntityTypeTag<WeeklyTaskSelection>()];
        tags.AddRange(selectionIds.Select(ApplicationCacheKey.EntityTag<WeeklyTaskSelection>));
        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private IQueryable<TaskItem> ActiveTopLevelTasks(string userId) =>
        context.Tasks
            .AsNoTracking()
            .Where(task => task.UserId == userId
                           && !task.IsArchived
                           && task.ParentTaskId == null
                           && !task.Project.IsArchived
                           && task.Project.Status == ProjectStatus.Active
                           && task.Status != TaskItemStatus.Done
                           && task.Status != TaskItemStatus.Archived);

    private IQueryable<ProjectOverviewProjection> BuildProjectOverviewQuery(
        string userId,
        DateTime today,
        DateTime weekStartDate,
        bool planningStatusesOnly = false)
    {
        var projects = context.Projects
            .AsNoTracking()
            .Where(project => project.UserId == userId);

        if (planningStatusesOnly)
            projects = projects.Where(project => !project.IsArchived && OverviewStatuses.Contains(project.Status));

        return projects.Select(project => new ProjectOverviewProjection(
                project.Id,
                project.Name,
                project.Emoji,
                project.Status,
                project.Priority,
                project.DueDate,
                project.DesiredOutcome,
                project.IsArchived,
                project.Tasks.Count(task =>
                    !task.IsArchived &&
                    task.Status != TaskItemStatus.Done &&
                    task.Status != TaskItemStatus.Archived),
                project.Tasks.Count(task =>
                    !task.IsArchived &&
                    task.Status != TaskItemStatus.Done &&
                    task.Status != TaskItemStatus.Archived &&
                    task.DueDate.HasValue &&
                    task.DueDate.Value.Date < today),
                project.Tasks.Count(task =>
                    context.WeeklyTaskSelections.Any(selection =>
                        selection.UserId == userId &&
                        selection.WeekStartDate == weekStartDate &&
                        selection.TaskId == task.Id)),
                project.RowVersion));
    }

    private static IQueryable<TaskProjection> ProjectTasks(
        IQueryable<TaskItem> tasks,
        DateTime today,
        DateTime weekEndDate) =>
        tasks.Select(task => new TaskProjection(
            task.Id,
            task.ProjectId,
            task.Project.Name,
            task.Project.Emoji,
            task.Project.Status,
            task.Project.IsArchived,
            task.Title,
            task.Status,
            task.Priority,
            task.DueDate,
            task.CompletedDate,
            task.Complexity,
            task.IsCurrentTask,
            task.IsArchived,
            task.ParentTaskId,
            task.Dependencies.Any(dependency =>
                dependency.DependsOnTask.IsArchived ||
                dependency.DependsOnTask.Status != TaskItemStatus.Done),
            task.Subtasks.Count(subtask =>
                !subtask.IsArchived &&
                subtask.Status != TaskItemStatus.Done &&
                subtask.Status != TaskItemStatus.Archived &&
                subtask.DueDate.HasValue &&
                subtask.DueDate.Value.Date < today),
            task.Subtasks.Count(subtask =>
                !subtask.IsArchived &&
                subtask.Status != TaskItemStatus.Done &&
                subtask.Status != TaskItemStatus.Archived &&
                subtask.DueDate.HasValue &&
                subtask.DueDate.Value.Date >= today &&
                subtask.DueDate.Value.Date <= weekEndDate),
            task.Subtasks
                .Where(subtask => !subtask.IsArchived
                                  && subtask.Status != TaskItemStatus.Done
                                  && subtask.Status != TaskItemStatus.Archived)
                .OrderBy(subtask => subtask.SortOrder)
                .ThenBy(subtask => subtask.DueDate)
                .Select(subtask => new WeekNextActionDto(
                    subtask.Id,
                    subtask.Title,
                    subtask.Status,
                    subtask.DueDate))
                .FirstOrDefault()));

    private static WeekTaskCardDto ToTaskCard(
        TaskProjection task,
        bool isSelectedForCurrentWeek = false,
        string? attentionReason = null,
        string? replanningReason = null)
    {
        var blockReason = GetSelectionBlockReason(task);

        return new WeekTaskCardDto(
            task.Id,
            task.ProjectId,
            task.ProjectName,
            NormalizeEmoji(task.ProjectEmoji),
            task.ProjectStatus,
            task.Title,
            task.Status,
            task.Priority,
            task.DueDate,
            task.CompletedDate,
            task.Complexity,
            task.IsCurrentFocus,
            isSelectedForCurrentWeek,
            blockReason is null,
            blockReason,
            task.OverdueSubtaskCount,
            task.DueThisWeekSubtaskCount,
            task.NextAction,
            task.HasUnresolvedDependency,
            attentionReason,
            replanningReason);
    }

    private static string? GetSelectionBlockReason(TaskProjection task)
    {
        if (task.ProjectIsArchived || task.ProjectStatus == ProjectStatus.Archived)
            return "Archived projects cannot receive new weekly commitments.";
        if (task.ProjectStatus == ProjectStatus.Completed)
            return "Completed projects cannot receive new weekly commitments.";
        if (task.ProjectStatus != ProjectStatus.Active)
            return task.ProjectStatus switch
            {
                ProjectStatus.NotStarted => "Activate this project before adding weekly commitments.",
                ProjectStatus.Blocked => "Blocked projects need to be unblocked before adding new weekly commitments.",
                ProjectStatus.Parked => "Parked projects need to be reactivated before adding new weekly commitments.",
                _ => "Only active projects can contribute new weekly commitments."
            };
        if (task.ParentTaskId.HasValue)
            return "Subtasks cannot be selected directly for the week.";
        if (task.IsTaskArchived || task.Status == TaskItemStatus.Archived)
            return "Archived tasks cannot be selected for the week.";
        if (task.Status == TaskItemStatus.Done)
            return "Completed tasks cannot be newly selected for the week.";
        if (task.Status == TaskItemStatus.Waiting)
            return "Waiting tasks cannot be selected until they become actionable.";
        if (task.HasUnresolvedDependency)
            return "Complete all prerequisite tasks before selecting this task for the week.";
        if (task.Status is not (TaskItemStatus.Todo or TaskItemStatus.InProgress))
            return "Only Todo or In Progress tasks can be selected for the week.";

        return null;
    }

    private static string? GetReplanningReason(TaskProjection task)
    {
        if (task.IsTaskArchived || task.Status == TaskItemStatus.Archived)
            return "Task was archived.";
        if (task.ProjectIsArchived || task.ProjectStatus == ProjectStatus.Archived)
            return "Project was archived.";
        if (task.ProjectStatus == ProjectStatus.Completed)
            return "Project was completed.";
        if (task.ProjectStatus != ProjectStatus.Active)
            return task.ProjectStatus switch
            {
                ProjectStatus.NotStarted => "Project is not started.",
                ProjectStatus.Blocked => "Project is blocked.",
                ProjectStatus.Parked => "Project is parked.",
                _ => "Project is not active."
            };
        if (task.Status == TaskItemStatus.Waiting)
            return "Task is waiting.";
        if (task.HasUnresolvedDependency)
            return "Task is blocked by unresolved prerequisites.";

        return null;
    }

    private static string GetOverdueAttentionReason(TaskProjection task)
    {
        return task.OverdueSubtaskCount == 1
            ? "Contains 1 overdue subtask."
            : $"Contains {task.OverdueSubtaskCount} overdue subtasks.";
    }

    private static string GetDueThisWeekAttentionReason(TaskProjection task)
    {
        return task.DueThisWeekSubtaskCount == 1
            ? "Contains 1 subtask due this week."
            : $"Contains {task.DueThisWeekSubtaskCount} subtasks due this week.";
    }

    private static string NormalizeEmoji(string? emoji) =>
        string.IsNullOrWhiteSpace(emoji) ? ProjectEmojiDefaults.DefaultEmoji : emoji.Trim();

    private sealed record ProjectOverviewProjection(
        Guid Id,
        string Name,
        string Emoji,
        ProjectStatus Status,
        ProjectPriority Priority,
        DateTime? DueDate,
        string? DesiredOutcome,
        bool IsArchived,
        int OpenTaskCount,
        int OverdueTaskCount,
        int WeeklySelectionCount,
        byte[]? RowVersion)
    {
        public WeekProjectOverviewDto ToDto() => new(
            Id,
            Name,
            string.IsNullOrWhiteSpace(Emoji) ? ProjectEmojiDefaults.DefaultEmoji : Emoji,
            Status,
            Priority,
            DueDate,
            DesiredOutcome,
            OpenTaskCount,
            OverdueTaskCount,
            WeeklySelectionCount,
            RowVersion);
    }

    private sealed record TaskProjection(
        Guid Id,
        Guid ProjectId,
        string ProjectName,
        string ProjectEmoji,
        ProjectStatus ProjectStatus,
        bool ProjectIsArchived,
        string Title,
        TaskItemStatus Status,
        TaskPriority Priority,
        DateTime? DueDate,
        DateTime? CompletedDate,
        TaskComplexity? Complexity,
        bool IsCurrentFocus,
        bool IsTaskArchived,
        Guid? ParentTaskId,
        bool HasUnresolvedDependency,
        int OverdueSubtaskCount,
        int DueThisWeekSubtaskCount,
        WeekNextActionDto? NextAction);
}
