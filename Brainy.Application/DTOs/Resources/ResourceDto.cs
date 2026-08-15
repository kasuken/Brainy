namespace Brainy.Application.DTOs.Resources;

using Brainy.Domain.Common;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Resource"/>.</summary>
public record ResourceDto(
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
    string Emoji = ResourceEmojiDefaults.DefaultEmoji,
    /// <summary>Concurrency token captured at load time; pass back on update or delete.</summary>
    byte[]? RowVersion = null,
    string? ArchivedReason = null);
