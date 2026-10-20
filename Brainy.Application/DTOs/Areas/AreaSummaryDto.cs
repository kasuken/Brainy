namespace Brainy.Application.DTOs.Areas;

using Brainy.Domain.Common;

/// <summary>
/// Area data enriched with related entity counts and health metrics — used by the Areas homepage.
/// </summary>
public record AreaSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string? Purpose,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int ActiveProjectCount,
    int OpenTaskCount,
    int OverdueTaskCount,
    int NoteCount,
    int IdeaCount,
    int ResourceCount,
    string Emoji = AreaEmojiDefaults.DefaultEmoji,
    string? ArchivedReason = null);
