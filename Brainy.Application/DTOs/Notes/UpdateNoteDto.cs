using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for updating an existing note.</summary>
public record UpdateNoteDto(
    Guid Id,
    string Title,
    string Content,
    string? AiSummary,
    NoteStatus Status,
    ParaCategory ParaCategory,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? ResourceId);
