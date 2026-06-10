using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// The central unit of knowledge. Holds the user's original content separately from
/// AI-generated summaries and user highlights.
/// </summary>
public class Note : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Original, user-authored content. Never overwritten by AI output.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Latest AI-generated summary for quick access. Always marked as AI-generated content.
    /// Full summary history with provenance is stored in <see cref="Summaries"/>.
    /// </summary>
    public string? AiSummary { get; set; }

    public NoteStatus Status { get; set; }

    public ParaCategory ParaCategory { get; set; }

    public Guid? SourceId { get; set; }

    public Source? Source { get; set; }

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public Guid? ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();

    public ICollection<Highlight> Highlights { get; set; } = new List<Highlight>();

    public ICollection<Summary> Summaries { get; set; } = new List<Summary>();

    public ICollection<ActionItem> ActionItems { get; set; } = new List<ActionItem>();

    /// <summary>Relationships where this note is the source.</summary>
    public ICollection<NoteRelationship> OutgoingRelationships { get; set; } = new List<NoteRelationship>();

    /// <summary>Relationships where this note is the target.</summary>
    public ICollection<NoteRelationship> IncomingRelationships { get; set; } = new List<NoteRelationship>();

    public ICollection<Output> Outputs { get; set; } = new List<Output>();

    /// <summary>When true, the note is pinned to the Favorites section on the Notes page.</summary>
    public bool IsFavorite { get; set; }
}
