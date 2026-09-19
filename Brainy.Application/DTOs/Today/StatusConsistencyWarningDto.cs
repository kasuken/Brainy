using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Today;

/// <summary>
/// A single detected inconsistency between a project's status and the status of the
/// tasks inside it (for example, in-progress work sitting in a project that is not
/// Active). Surfaced on the Today screen so the user can reconcile the mismatch.
/// </summary>
public record StatusConsistencyWarningDto(
    StatusConsistencyKind Kind,
    StatusConsistencySeverity Severity,
    Guid ProjectId,
    string ProjectName,
    ProjectStatus ProjectStatus,
    int Count,
    string Message);

/// <summary>
/// Classifies the kind of status mismatch detected between a project and its tasks.
/// </summary>
public enum StatusConsistencyKind
{
    /// <summary>Tasks are marked In Progress while the project is not Active (e.g. Not Started, Blocked, Parked).</summary>
    InProgressWorkInInactiveProject = 1,

    /// <summary>The project is marked Completed but still has unfinished (open) tasks.</summary>
    CompletedProjectWithOpenTasks = 2,

    /// <summary>The project is archived but still has open tasks that were never closed.</summary>
    ArchivedProjectWithOpenTasks = 3,

    /// <summary>The project is Active and has tasks, but every task is already Done.</summary>
    ActiveProjectAllTasksDone = 4,

    /// <summary>The project is still Not Started even though it already has completed work.</summary>
    NotStartedProjectWithProgress = 5
}

/// <summary>
/// How strongly a <see cref="StatusConsistencyWarningDto"/> should be surfaced.
/// </summary>
public enum StatusConsistencySeverity
{
    /// <summary>A gentle nudge — the state is harmless but worth tidying up.</summary>
    Info = 1,

    /// <summary>A genuine contradiction the user probably wants to reconcile.</summary>
    Warning = 2
}
