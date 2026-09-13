namespace Brainy.Application.DTOs.Calendar;

/// <summary>
/// Current-user view of their ICS feed token's lifecycle. Never carries the raw token —
/// only <see cref="CalendarFeedRegeneratedDto"/> (returned once, from regeneration) does.
/// </summary>
public record CalendarFeedTokenStatusDto(
    bool HasActiveToken,
    DateTime? CreatedAtUtc,
    DateTime? LastAccessedAtUtc,
    DateTime? RevokedAtUtc);
