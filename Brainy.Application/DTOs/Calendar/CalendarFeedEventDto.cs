using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Calendar;

/// <summary>
/// One deadline to render as an all-day <c>VEVENT</c> in the ICS feed. <see cref="Date"/> is
/// the user-calendar date exactly as stored (Task/Project/Milestone due dates are never UTC
/// instants — see AGENTS.md), so it must be written into the feed as-is, with no time-zone
/// conversion.
/// </summary>
public record CalendarFeedEventDto(
    Guid SourceId,
    CalendarFeedEventKind Kind,
    string Title,
    DateTime Date,
    string? ProjectName,
    string? AreaName);
