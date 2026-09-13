namespace Brainy.Domain.Enums;

/// <summary>Why a <see cref="Entities.NoteRevision"/> was captured.</summary>
public enum NoteRevisionReason
{
    /// <summary>A user typed or pasted a change into the note editor.</summary>
    UserEdit,

    /// <summary>Content was produced or rewritten by an AI provider.</summary>
    AiGeneration,

    /// <summary>The note (or this state of it) arrived via an external importer.</summary>
    Import,

    /// <summary>The change was captured offline and applied once the client reconnected.</summary>
    OfflineSync,

    /// <summary>An earlier revision was restored, creating this one as a copy of it.</summary>
    Restore
}
