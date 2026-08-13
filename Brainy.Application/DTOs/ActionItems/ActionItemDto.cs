using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.ActionItems;

/// <summary>Read model for an action distilled from a note.</summary>
public sealed record ActionItemDto(
    Guid Id,
    Guid NoteId,
    string Title,
    string? Description,
    ActionItemStatus Status,
    bool IsAiGenerated,
    string? Model,
    string? PromptVersion,
    Guid? TaskItemId,
    Guid? ProjectId,
    string? ProjectName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    byte[]? RowVersion);
