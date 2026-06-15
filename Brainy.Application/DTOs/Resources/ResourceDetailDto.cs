namespace Brainy.Application.DTOs.Resources;

using Brainy.Domain.Common;

/// <summary>Rich projection of a Resource including aggregated stats and linked notes.</summary>
public record ResourceDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? Topic,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    Guid? AreaId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<string> Tags,
    int NoteCount,
    IReadOnlyList<ResourceNoteDto> Notes,
    string Emoji = ResourceEmojiDefaults.DefaultEmoji);

public record ResourceNoteDto(Guid Id, string Title, DateTime UpdatedAtUtc);
