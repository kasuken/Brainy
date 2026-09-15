namespace Brainy.Application.DTOs.Tags;

/// <summary>
/// Read-only projection of a <see cref="Domain.Entities.Tag"/> for the tag management page,
/// with usage counts scoped to the current user's active (non-archived) notes and resources.
/// </summary>
public record TagDto(
    Guid Id,
    string Name,
    string? Color,
    int NoteCount,
    int ResourceCount,
    DateTime? LastUsedAtUtc,
    DateTime CreatedAtUtc)
{
    /// <summary>Total number of active notes and resources currently tagged.</summary>
    public int UsageCount => NoteCount + ResourceCount;

    /// <summary>True when no active note or resource references this tag.</summary>
    public bool IsUnused => UsageCount == 0;
}
