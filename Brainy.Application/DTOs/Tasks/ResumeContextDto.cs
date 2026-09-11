using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Which entity kind a <see cref="ResumeContextLinkedItemDto"/> points to.</summary>
public enum ResumeContextLinkedItemType
{
    Note = 0,
    Output = 1,
}

/// <summary>
/// The most recently touched note or output within the task's project, shown as the
/// "most recently linked" activity signal on the Resume Context panel. Scoped to the
/// current user; never another user's data.
/// </summary>
public record ResumeContextLinkedItemDto(
    Guid Id,
    string Title,
    ResumeContextLinkedItemType Type,
    DateTime UpdatedAtUtc);

/// <summary>
/// Read model backing the Resume Context panel (issue #305): everything a user needs to
/// reconstruct where they left off on their current-focus task without reconstructing it
/// from memory. Every field is derived from data the task's owner already has access to;
/// nothing here is AI-inferred.
/// </summary>
public record ResumeContextDto(
    Guid TaskId,
    TaskItemStatus TaskStatus,
    bool IsArchived,

    /// <summary>True when an incomplete prerequisite (<see cref="Brainy.Domain.Entities.TaskDependency"/>) blocks this task.</summary>
    bool IsBlocked,

    /// <summary>Title of the nearest unresolved prerequisite, when <see cref="IsBlocked"/> is true.</summary>
    string? UnresolvedDependencyTitle,

    /// <summary>True when the task has at least one non-archived subtask.</summary>
    bool HasSubtasks,

    /// <summary>Title of the first not-done subtask in order, when <see cref="HasSubtasks"/> is true.</summary>
    string? NextSubtaskTitle,

    /// <summary>The most recently updated note or output in the task's project, if any.</summary>
    ResumeContextLinkedItemDto? MostRecentLinkedItem,

    Guid ProjectId,
    string ProjectName,
    DateTime? ProjectDueDate,

    Guid? GoalId,
    string? GoalTitle,
    DateTime? GoalDueDate,

    /// <summary>User-authored handoff note, editable inline from the panel.</summary>
    string? RestartNote);
