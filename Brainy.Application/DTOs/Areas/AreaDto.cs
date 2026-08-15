namespace Brainy.Application.DTOs.Areas;

using Brainy.Domain.Common;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Area"/>.</summary>
public record AreaDto(
    Guid Id,
    string Name,
    string? Description,
    string? Purpose,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Emoji = AreaEmojiDefaults.DefaultEmoji,
    string? ArchivedReason = null);
