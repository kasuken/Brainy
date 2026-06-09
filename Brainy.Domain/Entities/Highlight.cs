namespace Brainy.Domain.Entities;

/// <summary>
/// A user-selected passage within a note, supporting progressive summarization layers.
/// </summary>
public class Highlight : BaseEntity
{
    public Guid NoteId { get; set; }

    public Note Note { get; set; } = null!;

    public string Text { get; set; } = string.Empty;

    /// <summary>Optional user annotation explaining why the passage matters.</summary>
    public string? Annotation { get; set; }

    /// <summary>Progressive summarization layer (e.g. 1 = bold, 2 = highlighted).</summary>
    public int Layer { get; set; } = 1;

    public int? StartOffset { get; set; }

    public int? EndOffset { get; set; }
}
