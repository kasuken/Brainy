using Brainy.Application.DTOs.Week;

namespace Brainy.Application.DTOs.WeeklyReview;

/// <summary>
/// The full guided weekly-review payload: the existing Week planning overview
/// (projects, selected tasks, overdue/due-this-week attention, carry-forward
/// candidates) composed with the two elements this review adds on top of it —
/// Inbox age bands and previously-useful material resurfaced for a fresh look.
/// </summary>
/// <param name="Week">The current-week planning overview, reused as-is from <c>IWeekService</c>.</param>
/// <param name="InboxAgeBands">Unprocessed Inbox notes grouped into age bands.</param>
/// <param name="InboxTotalCount">Total unprocessed Inbox note count across all bands.</param>
/// <param name="ResurfacedNotes">Previously-useful notes surfaced for revisiting, each with a rationale.</param>
public sealed record WeeklyReviewDto(
    WeekOverviewDto Week,
    IReadOnlyList<InboxAgeBandDto> InboxAgeBands,
    int InboxTotalCount,
    IReadOnlyList<ResurfacedNoteDto> ResurfacedNotes);
