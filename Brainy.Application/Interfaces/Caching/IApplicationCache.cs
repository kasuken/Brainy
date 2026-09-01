namespace Brainy.Application.Interfaces.Caching;

/// <summary>
/// Provides application-level caching with mandatory authenticated-user isolation.
/// Implementations may use local memory or a distributed cache such as Redis.
/// </summary>
public interface IApplicationCache
{
    /// <summary>
    /// Gets a value from the current user's cache or creates and stores it when absent.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="userId">The authenticated user's stable identifier.</param>
    /// <param name="key">The logical key within the user's cache namespace.</param>
    /// <param name="dependencyTags">
    /// One or more tags identifying persisted data read to produce the cached value.
    /// </param>
    /// <param name="valueFactory">Creates a detached, DTO-like value on a cache miss.</param>
    /// <param name="cancellationToken">A token that cancels value creation.</param>
    /// <returns>The cached or newly created value.</returns>
    Task<T> GetOrCreateAsync<T>(
        string userId,
        string key,
        IReadOnlyCollection<string> dependencyTags,
        Func<CancellationToken, Task<T>> valueFactory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached values for one authenticated user that depend on any supplied tag.
    /// </summary>
    /// <param name="userId">The authenticated user's stable identifier.</param>
    /// <param name="dependencyTags">One or more dependency tags to invalidate.</param>
    /// <param name="cancellationToken">A token that cancels the invalidation request.</param>
    /// <returns>A task representing the invalidation operation.</returns>
    ValueTask InvalidateTagsAsync(
        string userId,
        IReadOnlyCollection<string> dependencyTags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates every cached value for one authenticated user during full account erasure.
    /// </summary>
    /// <param name="userId">The authenticated user's stable identifier.</param>
    /// <param name="cancellationToken">A token that cancels the invalidation request.</param>
    /// <returns>A task representing the invalidation operation.</returns>
    ValueTask InvalidateUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
