using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Compact project summary used throughout the Week planning surface.
/// </summary>
/// <param name="Id">The project identifier.</param>
/// <param name="Name">The project name.</param>
/// <param name="Emoji">The project emoji.</param>
/// <param name="Status">The current lifecycle status.</param>
/// <param name="Priority">The current priority.</param>
/// <param name="DueDate">The optional due date.</param>
/// <param name="DesiredOutcome">The intended end state.</param>
/// <param name="OpenTaskCount">The number of unfinished, non-archived tasks.</param>
/// <param name="OverdueTaskCount">The number of overdue unfinished tasks.</param>
/// <param name="WeeklySelectionCount">The number of selected tasks in the current week.</param>
/// <param name="RowVersion">The optimistic concurrency token for quick status changes.</param>
public record WeekProjectOverviewDto(
    Guid Id,
    string Name,
    string Emoji,
    ProjectStatus Status,
    ProjectPriority Priority,
    DateTime? DueDate,
    string? DesiredOutcome,
    int OpenTaskCount,
    int OverdueTaskCount,
    int WeeklySelectionCount,
    byte[]? RowVersion = null);
