namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Selected tasks grouped by project for the current week.
/// </summary>
/// <param name="Project">The associated project.</param>
/// <param name="Tasks">The selected tasks for that project.</param>
public record WeekProjectPlanDto(
    WeekProjectOverviewDto Project,
    IReadOnlyList<WeekTaskCardDto> Tasks);
