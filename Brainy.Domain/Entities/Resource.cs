namespace Brainy.Domain.Entities;

/// <summary>
/// A topic of interest or reference material (PARA: Resource).
/// </summary>
public class Resource : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}
