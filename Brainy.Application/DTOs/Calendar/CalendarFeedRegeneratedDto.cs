namespace Brainy.Application.DTOs.Calendar;

/// <summary>
/// Returned exactly once, immediately after (re)generating a feed token. The Web layer
/// combines <see cref="RawToken"/> with its own base URL to build the subscription link;
/// the raw value itself is never persisted, logged, or included in telemetry.
/// </summary>
public record CalendarFeedRegeneratedDto(string RawToken, DateTime CreatedAtUtc);
