using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// An ongoing responsibility without a fixed end date (PARA: Area).
/// </summary>
public class Area : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The ongoing responsibility this area represents — the "why" behind it.</summary>
    public string? Purpose { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>When this area was archived. Null if active.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Idea> Ideas { get; set; } = new List<Idea>();

    public ICollection<Goal> Goals { get; set; } = new List<Goal>();
}
