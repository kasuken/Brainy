using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Inbox;

/// <summary>A heuristic PARA classification suggestion for an inbox note.</summary>
public record InboxSuggestionDto(
    ParaCategory SuggestedCategory,
    string Reasoning,
    /// <summary>Always false — this is a keyword heuristic, not an AI model.</summary>
    bool IsAiGenerated = false);
