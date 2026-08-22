namespace Brainy.Application.DTOs.Tasks;

/// <summary>
/// Defines which project lifecycle states are included in Tasks Hub queries.
/// </summary>
public enum TaskHubProjectScope
{
    /// <summary>Include tasks from active projects only.</summary>
    ActiveOnly = 0,

    /// <summary>Include tasks from active and blocked projects.</summary>
    ActiveAndBlocked = 1,

    /// <summary>Include tasks from active, blocked, and parked projects.</summary>
    ActiveBlockedAndParked = 2
}