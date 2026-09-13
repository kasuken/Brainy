using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Templates;

/// <summary>Read-only projection of a <see cref="Domain.Entities.NoteTemplate"/>.</summary>
public record NoteTemplateDto(
    Guid Id,
    string Name,
    string TitlePattern,
    string ContentScaffold,
    ParaCategory DefaultParaCategory,
    bool IsBuiltIn,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[]? RowVersion = null);

/// <summary>Payload for creating a new note template.</summary>
public record CreateNoteTemplateDto(
    string Name,
    string TitlePattern,
    string ContentScaffold = "",
    ParaCategory DefaultParaCategory = ParaCategory.Project);

/// <summary>Payload for updating a note template.</summary>
public record UpdateNoteTemplateDto(
    Guid Id,
    string Name,
    string TitlePattern,
    string ContentScaffold = "",
    ParaCategory DefaultParaCategory = ParaCategory.Project,
    byte[]? RowVersion = null);

/// <summary>
/// Instantiates a note template into a real note through the normal note creation
/// path, so it records an initial <see cref="Domain.Entities.NoteRevision"/> exactly
/// like a hand-made note.
/// </summary>
public record InstantiateNoteTemplateDto(
    Guid TemplateId,
    string? Title = null,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? ResourceId = null);

/// <summary>Saves an existing note as a reusable template.</summary>
public record SaveNoteAsTemplateDto(Guid NoteId, string TemplateName);
