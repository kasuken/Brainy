namespace Brainy.Domain.Enums;

/// <summary>
/// Selectable reporting window for the Pulse activity report.
/// </summary>
public enum PulsePeriod
{
    LastWeek = 0,
    LastTwoWeeks = 1,
    LastThreeWeeks = 2,
    LastMonth = 3,

    /// <summary>User-supplied start/end dates.</summary>
    Custom = 4,
}
