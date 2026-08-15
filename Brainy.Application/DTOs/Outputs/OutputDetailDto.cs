using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Full detail projection of an <see cref="Domain.Entities.Output"/>, including content and source notes.</summary>
public record OutputDetailDto(
    Guid Id,
    string Title,
    string? Description,
    string Content,
    OutputType Type,
    OutputStatus Status,
    bool IsAiGenerated,
    string? Model,
    string? PromptVersion,
    bool IsArchived,
    Guid? ProjectId,
    string? ProjectTitle,
    Guid? AreaId,
    string? AreaName,
    Guid? GoalId,
    string? GoalTitle,
    DateTime? PublishedDate,
    DateTime? ArchivedDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<OutputSourceNoteDto> SourceNotes,
    /// <summary>Concurrency token captured at load time; pass back on update or delete.</summary>
    byte[]? RowVersion = null,
    string? ArchivedReason = null);

/// <summary>Lightweight reference to a <see cref="Domain.Entities.Note"/> that was used as source material for an output.</summary>
public record OutputSourceNoteDto(Guid NoteId, string NoteTitle, DateTime CreatedAtUtc);
