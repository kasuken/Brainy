namespace Brainy.Domain.Entities;

/// <summary>
/// An ongoing responsibility without a fixed end date (PARA: Area).
/// </summary>
public class Area : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}
