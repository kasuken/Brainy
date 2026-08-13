using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Input data required to create a new <see cref="Domain.Entities.Output"/>.</summary>
public record CreateOutputDto(
    string Title,
    string? Description,
    OutputType Type,
    string Content = "",
    Guid? ProjectId = null,
    Guid? AreaId = null,
    Guid? GoalId = null,
    bool IsAiGenerated = false,
    string? Model = null,
    string? PromptVersion = null,
    IReadOnlyList<Guid>? SourceNoteIds = null);
