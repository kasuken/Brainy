namespace Brainy.Application.Common;

/// <summary>
/// Central clock helpers for the application layer. All due-date and scheduling
/// logic must resolve "today" through <see cref="GetUserToday"/> so every screen
/// (Today, Tasks Hub, Calendar, Projects, Goals) agrees on the current date.
/// </summary>
internal static class TimeProviderExtensions
{
    /// <summary>
    /// Returns the calendar date used for "due today" / "overdue" comparisons.
    /// Currently the UTC date; when per-user time zones are introduced, apply the
    /// user's time-zone conversion here — this is the single definition of "today".
    /// </summary>
    public static DateTime GetUserToday(this TimeProvider timeProvider) =>
        timeProvider.GetUtcNow().UtcDateTime.Date;
}
