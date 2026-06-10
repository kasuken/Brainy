using Brainy.Application.DTOs.Calendar;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for the Tasks Calendar view.</summary>
public interface ICalendarService
{
    /// <summary>
    /// Returns all non-archived, non-done active tasks that have a due date, filtered as requested.
    /// Tasks from archived projects are also excluded.
    /// </summary>
    Task<IReadOnlyList<CalendarTaskDto>> GetCalendarTasksAsync(
        CalendarFilterDto? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns upcoming deadlines grouped into today / this week / overdue.</summary>
    Task<UpcomingDeadlinesDto> GetUpcomingDeadlinesAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates a task's due date (called from drag-and-drop rescheduling).</summary>
    Task RescheduleDueDateAsync(Guid taskId, DateTime newDueDate, CancellationToken cancellationToken = default);

    /// <summary>Returns the count of tasks per date for workload indicators.</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetWorkloadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
