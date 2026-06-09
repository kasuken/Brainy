using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// The original origin of a captured note, preserved for provenance and citation.
/// </summary>
public class Source : BaseEntity
{
    public SourceType Type { get; set; }

    public string? Title { get; set; }

    public string? Url { get; set; }

    public string? Author { get; set; }

    /// <summary>Free-form citation or reference to the original material.</summary>
    public string? Reference { get; set; }

    public DateTime? CapturedAtUtc { get; set; }

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}
