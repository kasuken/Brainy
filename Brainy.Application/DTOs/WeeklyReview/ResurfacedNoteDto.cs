namespace Brainy.Application.DTOs.WeeklyReview;

/// <summary>
/// A previously-useful note the guided weekly review surfaces for a fresh look, together
/// with the explicit, human-readable reason it was chosen (acceptance criterion: every
/// resurfaced item must identify its project, goal, topic, or recency rationale).
/// </summary>
public sealed record ResurfacedNoteDto(
    Guid NoteId,
    string Title,
    string Rationale,
    DateTime LastTouchedAtUtc,
    bool IsFavorite,
    Guid? ProjectId,
    string? ProjectName,
    Guid? AreaId,
    string? AreaName);
