namespace Brainy.Domain.Entities;

/// <summary>
/// A lightweight label used to organize and retrieve notes across PARA categories.
/// </summary>
public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}
