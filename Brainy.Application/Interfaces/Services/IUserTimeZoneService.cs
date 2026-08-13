namespace Brainy.Application.Interfaces.Services;

/// <summary>Resolves and persists the current user's calendar time zone.</summary>
public interface IUserTimeZoneService
{
    /// <summary>Returns the validated IANA time-zone id, defaulting safely to UTC.</summary>
    Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the resolved platform time-zone object.</summary>
    Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the user's current local calendar date.</summary>
    Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the current user's time zone after validating the identifier.</summary>
    Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default);

    /// <summary>Converts an inclusive local date range to an exclusive UTC range.</summary>
    Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(
        DateTime localStartDate,
        DateTime localEndDate,
        CancellationToken cancellationToken = default);
}
