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
    bool IsFavorite = false);
