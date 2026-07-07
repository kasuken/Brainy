using Brainy.Application.Common;
using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
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
    TimeProvider timeProvider) : ICalendarService
{
    // ── Public API ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CalendarTaskDto>> GetCalendarTasksAsync(
        CalendarFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = timeProvider.GetUserToday();

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
                t.DueDate!.Value < today && t.Status != TaskItemStatus.Done))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UpcomingDeadlinesDto> GetUpcomingDeadlinesAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today  = timeProvider.GetUserToday();

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
                t.DueDate!.Value < today && t.Status != TaskItemStatus.Done))
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
    }

    public async Task<IReadOnlyDictionary<DateOnly, int>> GetWorkloadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var userId    = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var fromDate  = from.ToDateTime(TimeOnly.MinValue);
        var toDateEnd = to.ToDateTime(TimeOnly.MaxValue);

        var groups = await ActiveBase(userId)
            .Where(t => t.DueDate != null && t.DueDate.Value >= fromDate && t.DueDate.Value <= toDateEnd)
            .GroupBy(t => t.DueDate!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups.ToDictionary(
            g => DateOnly.FromDateTime(g.Date),
            g => g.Count);
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
