namespace Brainy.Application.DTOs.WeeklyReview;

/// <summary>One age bucket of unprocessed Inbox notes (e.g. "Today", "1-4 weeks").</summary>
/// <param name="Key">Stable identifier for the band, for ordering and testing.</param>
/// <param name="Label">Display label for the band.</param>
/// <param name="Items">Notes falling into this age band, oldest first.</param>
public sealed record InboxAgeBandDto(
    string Key,
    string Label,
    IReadOnlyList<InboxAgeBandItemDto> Items);

/// <summary>One unprocessed Inbox note as shown in an age band.</summary>
public sealed record InboxAgeBandItemDto(
    Guid NoteId,
    string Title,
    DateTime CreatedAtUtc);
