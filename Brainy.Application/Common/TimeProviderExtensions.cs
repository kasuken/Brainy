namespace Brainy.Application.Common;

/// <summary>
/// Central clock helpers for the application layer. All due-date and scheduling
/// logic must resolve "today" through <see cref="GetUserToday"/> so every screen
/// (Today, Tasks Hub, Calendar, Projects, Goals) agrees on the current date.
/// </summary>
internal static class TimeProviderExtensions
{
    /// <summary>
    /// Returns the calendar date used for "due today" / "overdue" comparisons in
    /// the supplied user time zone.
    /// </summary>
    public static DateTime GetUserToday(this TimeProvider timeProvider, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).Date;

    /// <summary>Converts a local midnight to UTC, respecting daylight-saving rules.</summary>
    public static DateTime LocalDateToUtc(DateTime localDate, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }
}
