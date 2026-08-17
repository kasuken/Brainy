namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle stage of a <see cref="Entities.Project"/>.
/// Only <see cref="Active"/> projects appear in active work views (Today, task pickers, etc.).
/// </summary>
public enum ProjectStatus
{
    NotStarted = 0,
    Active = 1,
    /// <summary>Blocked by someone or something outside the user's control.</summary>
    Blocked = 2,
    /// <summary>Intentionally parked/deprioritized by the user, not blocked externally.</summary>
    Parked = 5,
    Completed = 3,
    Archived = 4
}
