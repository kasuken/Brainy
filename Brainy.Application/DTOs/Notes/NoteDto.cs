using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Note"/>.</summary>
public record NoteDto(
    Guid Id,
    string Title,
    string Content,
    string? AiSummary,
    NoteStatus Status,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime? ProcessedAtUtc,
    ParaCategory ParaCategory,
    Guid? SourceId,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? ResourceId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsFavorite = false,
    bool HasImages = false,
    /// <summary>URL of the linked <see cref="Domain.Entities.Source"/>, if any.</summary>
    string? SourceUrl = null,
    /// <summary>Display title of the linked <see cref="Domain.Entities.Source"/>, if any.</summary>
    string? SourceTitle = null,
    /// <summary>Concurrency token captured at load time; pass back on update to detect conflicts.</summary>
    byte[]? RowVersion = null,
    /// <summary>Normalized display names of tags assigned to the note.</summary>
    IReadOnlyList<string>? Tags = null);
