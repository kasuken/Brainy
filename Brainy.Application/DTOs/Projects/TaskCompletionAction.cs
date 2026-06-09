namespace Brainy.Application.DTOs.Projects;

/// <summary>
/// Specifies how open tasks are handled when a project is marked as completed.
/// </summary>
public enum TaskCompletionAction
{
    /// <summary>Open tasks are left in their current state.</summary>
    LeaveAsIs,

    /// <summary>All non-archived open tasks are marked as Done.</summary>
    CompleteAll,

    /// <summary>All non-archived open tasks are soft-archived.</summary>
    ArchiveAll,
}
