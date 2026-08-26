using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for reviewing and processing a note out of the Inbox.</summary>
public record ProcessNoteDto(
    Guid Id,
    ParaCategory ParaCategory,
    NoteStatus Status = NoteStatus.Active,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? ResourceId = null,
    string? Title = null,
    string? Content = null,
    byte[]? RowVersion = null);
