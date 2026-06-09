namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle stage of a <see cref="Entities.Project"/>.
/// Only <see cref="Active"/> projects appear in active work views (Today, task pickers, etc.).
/// </summary>
public enum ProjectStatus
{
    NotStarted = 0,
    Active = 1,
    Waiting = 2,
    Completed = 3,
    Archived = 4
}
