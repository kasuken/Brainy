using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Bounded task-picker payload for one project on the Week page.
/// </summary>
public record WeekTaskPickerDto(
    Guid ProjectId,
    string ProjectName,
    string ProjectEmoji,
    ProjectStatus ProjectStatus,
    string? SearchTerm,
    IReadOnlyList<WeekTaskCardDto> Tasks);
