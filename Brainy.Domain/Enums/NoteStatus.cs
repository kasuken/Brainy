namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle status of a note within the CODE workflow.
/// </summary>
public enum NoteStatus
{
    /// <summary>Captured but not yet processed.</summary>
    Inbox = 0,

    /// <summary>Processed and in active use.</summary>
    Active = 1,

    /// <summary>Distilled with highlights and summaries.</summary>
    Distilled = 2,

    /// <summary>Inactive but kept for reference.</summary>
    Archived = 3
}
