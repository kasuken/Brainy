namespace Brainy.Application.DTOs.Summaries;

/// <summary>Read model for a single summary layer on a note.</summary>
public record SummaryDto(
    Guid Id,
    Guid NoteId,
    string Content,
    bool IsAiGenerated,
    string? Model,
    string? PromptVersion,
    DateTime CreatedAtUtc);
