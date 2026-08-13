using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// An actionable item distilled from a note. Can be promoted into a task.
/// </summary>
public class ActionItem : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ActionItemStatus Status { get; set; }

    /// <summary>True when extracted by AI rather than entered by the user.</summary>
    public bool IsAiGenerated { get; set; }

    /// <summary>The AI model used for extraction, when applicable.</summary>
    public string? Model { get; set; }

    /// <summary>The versioned extraction prompt, when applicable.</summary>
    public string? PromptVersion { get; set; }

    public Guid? NoteId { get; set; }

    public Note? Note { get; set; }

    /// <summary>Set when this action item has been promoted to a task.</summary>
    public Guid? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }
}
