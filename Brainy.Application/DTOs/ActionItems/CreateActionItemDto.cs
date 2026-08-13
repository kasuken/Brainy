namespace Brainy.Application.DTOs.ActionItems;

/// <summary>Payload for manually distilling an action from a note.</summary>
public sealed record CreateActionItemDto(
    Guid NoteId,
    string Title,
    string? Description = null);
