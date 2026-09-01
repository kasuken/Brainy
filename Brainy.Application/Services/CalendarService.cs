using System.Globalization;
using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Provides aggregated task data for the Tasks Calendar view.
/// All queries exclude archived tasks and tasks belonging to archived projects.
/// Read-only queries use <c>AsNoTracking</c> for performance.
/// </summary>
internal sealed class CalendarService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    IApplicationCache cache) : ICalendarService
{
    // ── Public API ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CalendarTaskDto>> GetCalendarTasksAsync(
        CalendarFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = ApplicationCacheKey.Create(
            "calendar",
            "tasks",
            today.Date,
            filter?.ProjectId,
            filter?.AreaId,
            filter?.Priority,
            filter?.Status,
            filter?.SearchTerm);
        return await cache.GetOrCreateAsync(
            userId,
            cacheKey,
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTypeTag<TaskDependency>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            ct => GetCalendarTasksCoreAsync(userId, today, filter, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CalendarTaskDto>> GetCalendarTasksCoreAsync(
        string userId,
        DateTime today,
        CalendarFilterDto? filter,
        CancellationToken cancellationToken)
    {
        var query = ActiveBase(userId).Where(t => t.DueDate != null);

        if (filter is not null)
        {
            if (filter.ProjectId.HasValue)
                query = query.Where(t => t.ProjectId == filter.ProjectId.Value);

            if (filter.AreaId.HasValue)
                query = query.Where(t => t.Project.AreaId == filter.AreaId.Value);

            if (filter.Priority.HasValue)
                query = query.Where(t => t.Priority == filter.Priority.Value);

            if (filter.Status.HasValue)
                query = query.Where(t => t.Status == filter.Status.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(t => t.Title.ToLower().Contains(term));
            }
        }

        return await query
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Select(t => new CalendarTaskDto(
                t.Id,
                t.Title,
                t.Status,
                t.Priority,
                t.DueDate,
                t.ProjectId,
                t.Project.Name,
                t.Project.AreaId,
                t.Project.Area != null ? t.Project.Area.Name : null,
                t.DueDate!.Value < today && t.Status != TaskItemStatus.Done,
                t.Complexity,
                t.Dependencies.Count(),
                t.Dependencies.Count(d => d.DependsOnTask.IsArchived || d.DependsOnTask.Status != TaskItemStatus.Done)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UpcomingDeadlinesDto> GetUpcomingDeadlinesAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"calendar:upcoming:{today:yyyy-MM-dd}",
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<Area>(),
                ApplicationCacheKey.EntityTypeTag<TaskDependency>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            ct => GetUpcomingDeadlinesCoreAsync(userId, today, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpcomingDeadlinesDto> GetUpcomingDeadlinesCoreAsync(
        string userId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        // Monday of current week
        var dayOfWeek    = (int)today.DayOfWeek;
        var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek; // Sunday = 0 in DayOfWeek
        var weekStart    = today.AddDays(mondayOffset);
        var weekEnd      = weekStart.AddDays(6);                  // Sunday inclusive

        var all = await ActiveBase(userId)
            .Where(t => t.DueDate != null &&
                        (t.DueDate.Value < today ||
                         (t.DueDate.Value >= today && t.DueDate.Value <= weekEnd)))
            .Select(t => new CalendarTaskDto(
                t.Id,
                t.Title,
                t.Status,
                t.Priority,
                t.DueDate,
                t.ProjectId,
                t.Project.Name,
                t.Project.AreaId,
                t.Project.Area != null ? t.Project.Area.Name : null,
                t.DueDate!.Value < today && t.Status != TaskItemStatus.Done,
                t.Complexity,
                t.Dependencies.Count(),
                t.Dependencies.Count(d => d.DependsOnTask.IsArchived || d.DependsOnTask.Status != TaskItemStatus.Done)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dueToday   = all.Where(t => t.DueDate!.Value.Date == today).ToList();
        var dueThisWeek = all
            .Where(t => t.DueDate!.Value.Date > today && t.DueDate!.Value.Date <= weekEnd)
            .ToList();
        var overdue    = all.Where(t => t.IsOverdue).OrderBy(t => t.DueDate).ToList();

        return new UpcomingDeadlinesDto(dueToday, dueThisWeek, overdue);
    }

    public async Task RescheduleDueDateAsync(
        Guid taskId,
        DateTime newDueDate,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        task.DueDate = newDueDate.Date;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTag<TaskItem>(task.Id)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetWorkloadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var userId    = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var fromDate  = from.ToDateTime(TimeOnly.MinValue);
        var toDateEnd = to.ToDateTime(TimeOnly.MaxValue);

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"calendar:workload:{from:O}:{to:O}");
        return await cache.GetOrCreateAsync(
            userId,
            key,
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.TimeZoneTag
            ],
            async ct =>
            {
                var groups = await ActiveBase(userId)
                    .Where(t => t.DueDate != null && t.DueDate.Value >= fromDate && t.DueDate.Value <= toDateEnd)
                    .GroupBy(t => t.DueDate!.Value.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlyDictionary<DateOnly, int>)groups.ToDictionary(
                    g => DateOnly.FromDateTime(g.Date),
                    g => g.Count);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Base query: current user's tasks, non-archived, non-done, non-subtasks,
    /// from non-archived projects. Includes Project and Project.Area navigation.
    /// </summary>
    private IQueryable<Domain.Entities.TaskItem> ActiveBase(string userId) =>
        context.Tasks
            .AsNoTracking()
            .Include(t => t.Project)
                .ThenInclude(p => p.Area)
            .Where(t =>
                t.UserId == userId &&
                !t.IsArchived &&
                !t.Project.IsArchived &&
                t.Status != TaskItemStatus.Done &&
                t.Status != TaskItemStatus.Archived);
}
