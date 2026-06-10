namespace Brainy.Application.DTOs.Calendar;

/// <summary>Tasks grouped by deadline urgency for the upcoming deadlines panel.</summary>
public record UpcomingDeadlinesDto(
    IReadOnlyList<CalendarTaskDto> DueToday,
    IReadOnlyList<CalendarTaskDto> DueThisWeek,
    IReadOnlyList<CalendarTaskDto> Overdue);
