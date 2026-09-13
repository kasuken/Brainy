using Brainy.Application.DTOs.Calendar;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Reads the deadlines exposed by the ICS calendar feed for a specific, already-resolved
/// user. Deliberately takes <paramref name="userId"/> explicitly rather than reading it from
/// <c>ICurrentUserService</c>: the feed endpoint is anonymous (calendar clients cannot
/// perform an interactive login) and identifies the caller purely by validating the feed
/// token via <see cref="ICalendarFeedTokenService"/>, so there is no ambient authenticated
/// principal to read from.
/// </summary>
public interface ICalendarFeedService
{
    /// <summary>
    /// Returns tasks with due dates, project deadlines, and dated goal milestones for
    /// <paramref name="userId"/>. Archived items (and items under an archived parent) are
    /// always excluded, and completed/done items are excluded, consistent with every other
    /// active workflow (see AGENTS.md).
    /// </summary>
    Task<IReadOnlyList<CalendarFeedEventDto>> GetFeedEventsAsync(
        string userId,
        CalendarFilterDto? filter = null,
        CancellationToken cancellationToken = default);
}
