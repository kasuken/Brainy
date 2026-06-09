namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle stage of a <see cref="Entities.Project"/>.
/// </summary>
public enum ProjectStatus
{
    Planning = 0,
    Active = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4
}
