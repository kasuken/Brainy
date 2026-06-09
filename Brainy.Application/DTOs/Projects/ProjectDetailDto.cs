using Brainy.Application.DTOs.Notes;
using Brainy.Application.DTOs.Tasks;
using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Projects;

/// <summary>
/// Full project workspace data — used by the Project Detail page.
/// Aggregates the project header, tasks (top-level only), notes, and resource notes.
/// </summary>
public record ProjectDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? DesiredOutcome,
    ProjectStatus Status,
    ProjectPriority Priority,
    DateTime? StartDate,
    DateTime? DueDate,
    DateTime? CompletedDate,
    bool IsArchived,
    Guid? AreaId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc,
    int TotalTaskCount,
    int OpenTaskCount,
    int DoneTaskCount,
    double ProgressPercent,
    IReadOnlyList<TaskItemDto> Tasks,
    IReadOnlyList<NoteDto> Notes,
    IReadOnlyList<NoteDto> ResourceNotes);
