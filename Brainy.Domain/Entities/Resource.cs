using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A topic of interest or reference material (PARA: Resource).
/// </summary>
public class Resource : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Visual identifier chosen by the user (for example: 📚, 🧪).</summary>
    public string Emoji { get; set; } = ResourceEmojiDefaults.DefaultEmoji;

    public string? Description { get; set; }

    /// <summary>Subject or domain of this resource (e.g. "Machine Learning", "Finance").</summary>
    public string? Topic { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>UTC timestamp of when this resource was archived; null when active.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
