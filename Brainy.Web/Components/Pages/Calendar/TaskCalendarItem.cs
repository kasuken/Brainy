using Brainy.Domain.Enums;
using Heron.MudCalendar;

namespace Brainy.Web.Components.Pages.Calendar;

/// <summary>
/// Calendar event item that extends <see cref="CalendarItem"/> to carry task-specific data
/// needed for rendering and drag-and-drop operations.
/// </summary>
public sealed class TaskCalendarItem : CalendarItem
{
    public Guid TaskId { get; set; }
    public TaskPriority Priority { get; set; }
    public TaskComplexity? Complexity { get; set; }
    public int DependencyCount { get; set; }
    public int UnresolvedDependencyCount { get; set; }
    public TaskItemStatus Status { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid? AreaId { get; set; }
    public string? AreaName { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsBlocked => UnresolvedDependencyCount > 0;

    /// <summary>CSS class suffix used to apply priority-based border/background styling.</summary>
    public string CssClass => Priority switch
    {
        TaskPriority.Critical => "cal-event--critical",
        TaskPriority.High     => "cal-event--high",
        TaskPriority.Medium   => "cal-event--medium",
        _                     => "cal-event--low",
    };
}
