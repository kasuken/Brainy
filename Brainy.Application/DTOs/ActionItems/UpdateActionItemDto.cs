using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.ActionItems;

/// <summary>Payload for editing a distilled action.</summary>
public sealed record UpdateActionItemDto(
    Guid Id,
    string Title,
    string? Description,
    ActionItemStatus Status,
    byte[]? RowVersion = null);
