using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for creating a new note.</summary>
public record CreateNoteDto(
    string Title,
    string Content = "",
    ParaCategory ParaCategory = ParaCategory.Project,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? ResourceId = null);
