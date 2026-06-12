namespace Brainy.Application.DTOs.Summaries;

/// <summary>Payload for creating a new manual summary layer on a note.</summary>
public record CreateSummaryDto(
    Guid NoteId,
    string Content,
    bool IsAiGenerated = false,
    string? Model = null,
    string? PromptVersion = null);
