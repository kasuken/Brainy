using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for processing a note out of the Inbox into an active PARA category.</summary>
public record ProcessNoteDto(
    Guid Id,
    ParaCategory ParaCategory,
    NoteStatus Status = NoteStatus.Active,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? ResourceId = null);
