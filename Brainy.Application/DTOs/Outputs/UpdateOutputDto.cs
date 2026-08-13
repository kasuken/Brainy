using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Input data required to update an existing <see cref="Domain.Entities.Output"/>.</summary>
public record UpdateOutputDto(
    Guid Id,
    string Title,
    string? Description,
    OutputType Type,
    OutputStatus Status,
    string Content,
    Guid? ProjectId,
    Guid? AreaId,
    Guid? GoalId,
    bool? IsAiGenerated = null,
    string? Model = null,
    string? PromptVersion = null,
    IReadOnlyList<Guid>? SourceNoteIds = null,
    /// <summary>
    /// Concurrency token from the loaded output. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the output changed after it was loaded.
    /// </summary>
    byte[]? RowVersion = null);
