using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A lightweight label used to organize and retrieve notes across PARA categories.
/// </summary>
public class Tag : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
}
