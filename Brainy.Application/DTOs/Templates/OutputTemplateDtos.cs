using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Templates;

/// <summary>Read-only projection of a <see cref="Domain.Entities.OutputTemplate"/>.</summary>
public record OutputTemplateDto(
    Guid Id,
    string Name,
    string TitlePattern,
    OutputType Type,
    string ContentScaffold,
    OutputTemplateSourceSelectionMode DefaultSourceSelection,
    bool IsBuiltIn,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[]? RowVersion = null);

/// <summary>Payload for creating a new output template.</summary>
public record CreateOutputTemplateDto(
    string Name,
    string TitlePattern,
    OutputType Type,
    string ContentScaffold = "",
    OutputTemplateSourceSelectionMode DefaultSourceSelection = OutputTemplateSourceSelectionMode.None);

/// <summary>Payload for updating an output template.</summary>
public record UpdateOutputTemplateDto(
    Guid Id,
    string Name,
    string TitlePattern,
    OutputType Type,
    string ContentScaffold = "",
    OutputTemplateSourceSelectionMode DefaultSourceSelection = OutputTemplateSourceSelectionMode.None,
    byte[]? RowVersion = null);

/// <summary>
/// Instantiates an output template into a real output through the normal output
/// creation path. When the template's <see cref="OutputTemplateSourceSelectionMode"/>
/// calls for it and a project is supplied, that project's active notes are resolved
/// and passed as plain source-note ids — the same field a hand-made output would set.
/// </summary>
public record InstantiateOutputTemplateDto(
    Guid TemplateId,
    string? Title = null,
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? GoalId = null);

/// <summary>Saves an existing output as a reusable template.</summary>
public record SaveOutputAsTemplateDto(Guid OutputId, string TemplateName);
