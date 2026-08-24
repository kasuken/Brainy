using System.Globalization;

namespace Brainy.Application.Common;

/// <summary>
/// Normalizes user-calendar dates to Brainy's Monday-Sunday working-week model.
/// </summary>
internal static class WeekDateHelper
{
    /// <summary>
    /// Returns the Monday-Sunday window that contains <paramref name="userToday"/>.
    /// </summary>
    /// <param name="userToday">The user's current calendar date.</param>
    /// <returns>The normalized week window.</returns>
    public static WeekWindow GetWeekContaining(DateTime userToday)
    {
        var normalizedToday = userToday.Date;
        var offset = normalizedToday.DayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };

        var weekStart = normalizedToday.AddDays(-offset);
        var weekEnd = weekStart.AddDays(6);
        return new WeekWindow(weekStart, weekEnd, ISOWeek.GetWeekOfYear(weekStart));
    }
}

/// <summary>
/// A normalized Monday-Sunday week window in the user's calendar.
/// </summary>
/// <param name="WeekStartDate">The Monday date.</param>
/// <param name="WeekEndDate">The Sunday date.</param>
/// <param name="WeekNumber">The ISO week number for the Monday date.</param>
internal readonly record struct WeekWindow(
    DateTime WeekStartDate,
    DateTime WeekEndDate,
    int WeekNumber)
{
    /// <summary>Gets the Monday of the immediately previous planning week.</summary>
    public DateTime PreviousWeekStartDate => WeekStartDate.AddDays(-7);
}
