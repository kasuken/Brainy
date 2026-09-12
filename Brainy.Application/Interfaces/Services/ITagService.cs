using Brainy.Application.DTOs.Tags;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Application service for managing <see cref="Domain.Entities.Tag"/> entities: usage stats
/// for the tag management page, rename, merge, delete, and bulk removal of unused tags.
/// </summary>
public interface ITagService
{
    /// <summary>
    /// Returns every tag owned by the current user, with usage counts and the last-used
    /// date derived from the active notes and resources that reference it.
    /// </summary>
    Task<IReadOnlyList<TagDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag in place. Every note and resource using it keeps the same association,
    /// now shown under the new name. The new name must not collide (case-insensitively)
    /// with another of the user's tags; merge instead when it does.
    /// </summary>
    Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges <paramref name="sourceId"/> into <paramref name="targetId"/>: every note and
    /// resource tagged with the source is retagged with the target (without duplicating
    /// existing associations), then the source tag is removed.
    /// </summary>
    Task MergeAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tag, detaching it from every note and resource without deleting them.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every tag owned by the current user that has no active note or resource
    /// attached. Returns the number of tags removed.
    /// </summary>
    Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default);
}
