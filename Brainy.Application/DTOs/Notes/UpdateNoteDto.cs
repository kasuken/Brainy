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
    Guid? ResourceId,
    /// <summary>
    /// Source URL to set or update. Pass an empty string to clear the source.
    /// Pass <c>null</c> (default) to leave the existing source unchanged.
    /// </summary>
    string? SourceUrl = null,
    /// <summary>Human-readable title for the source. Ignored when <see cref="SourceUrl"/> is null.</summary>
    string? SourceTitle = null);