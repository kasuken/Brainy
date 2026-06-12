using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for creating a new note.</summary>
public record CreateNoteDto(
    string Title,
    string Content = "",
    ParaCategory ParaCategory = ParaCategory.Project,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? ResourceId = null,
    NoteStatus Status = NoteStatus.Inbox,
    /// <summary>
    /// Optional URL of the source the note was captured from. When supplied, a
    /// <see cref="Domain.Entities.Source"/> record is created and linked to the note.
    /// </summary>
    string? SourceUrl = null,
    /// <summary>Human-readable title for the source (e.g. page title, article name).</summary>
    string? SourceTitle = null);