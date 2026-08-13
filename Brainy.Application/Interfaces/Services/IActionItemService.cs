using Brainy.Application.DTOs.ActionItems;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Manages actionable knowledge distilled from notes.</summary>
public interface IActionItemService
{
    /// <summary>Returns actions belonging to the current user's note.</summary>
    Task<IReadOnlyList<ActionItemDto>> GetByNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Creates a user-authored action on a note owned by the current user.</summary>
    Task<ActionItemDto> CreateAsync(CreateActionItemDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates title, description, and status without changing provenance or promotion.</summary>
    Task<ActionItemDto> UpdateAsync(UpdateActionItemDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes an action without deleting an already-promoted task.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts actions from the persisted note content using the configured AI provider,
    /// stores provider provenance, and skips duplicate titles already on the note.
    /// </summary>
    Task<IReadOnlyList<ActionItemDto>> ExtractFromNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes an action to a task in an active project owned by the current user.
    /// Repeated calls return the existing linked task instead of creating duplicates.
    /// </summary>
    Task<ActionItemDto> PromoteToTaskAsync(Guid id, Guid projectId, CancellationToken cancellationToken = default);
}
