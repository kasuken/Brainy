using System.Collections.Concurrent;
using Brainy.Application.Interfaces.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Brainy.Application.Caching;

/// <summary>
/// Stores short-lived application read models in the process-local memory cache.
/// </summary>
internal sealed class MemoryApplicationCache : IApplicationCache, IDisposable
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(5);
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });
    private readonly ConcurrentDictionary<string, UserCacheScope> _userScopes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ScopedCacheKey, FactoryLock> _factoryLocks = [];

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string userId,
        string key,
        IReadOnlyCollection<string> dependencyTags,
        Func<CancellationToken, Task<T>> valueFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueFactory);

        var normalizedTags = NormalizeTags(dependencyTags);
        cancellationToken.ThrowIfCancellationRequested();

        var scopedKey = new ScopedCacheKey(userId, key, typeof(T));
        if (TryGetValue(scopedKey, normalizedTags, out T? cachedValue))
            return cachedValue!;

        using var factoryLock = await AcquireFactoryLockAsync(scopedKey, cancellationToken)
            .ConfigureAwait(false);
        if (TryGetValue(scopedKey, normalizedTags, out cachedValue))
            return cachedValue!;

        UserCacheScope scope;
        long generation;
        while (true)
        {
            scope = _userScopes.GetOrAdd(userId, static _ => new UserCacheScope());
            if (scope.TryBeginFactory(out generation))
                break;

            RemoveScope(userId, scope);
        }

        try
        {
            var value = await valueFactory(cancellationToken).ConfigureAwait(false);
            var registration = new CacheEntryRegistration(scope, scopedKey, normalizedTags);
            scope.TryPublish(
                generation,
                registration,
                () => _memoryCache.Set(
                    scopedKey,
                    new CachedValue<T>(value, registration),
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = EntryLifetime,
                        Size = 1,
                        PostEvictionCallbacks =
                        {
                            new PostEvictionCallbackRegistration
                            {
                                EvictionCallback = (_, _, _, _) => ReleaseEntry(registration)
                            }
                        }
                    }));

            return value;
        }
        finally
        {
            scope.EndFactory();
            RemoveScopeIfIdle(userId, scope);
        }
    }

    /// <inheritdoc />
    public ValueTask InvalidateTagsAsync(
        string userId,
        IReadOnlyCollection<string> dependencyTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var normalizedTags = NormalizeTags(dependencyTags);
        cancellationToken.ThrowIfCancellationRequested();

        while (_userScopes.TryGetValue(userId, out var scope))
        {
            foreach (var key in scope.InvalidateTags(normalizedTags))
                _memoryCache.Remove(key);

            RemoveScopeIfIdle(userId, scope);

            if (!_userScopes.TryGetValue(userId, out var currentScope) ||
                ReferenceEquals(currentScope, scope))
            {
                break;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask InvalidateUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_userScopes.TryGetValue(userId, out var scope))
            return ValueTask.CompletedTask;

        var keys = scope.Retire();
        RemoveScope(userId, scope);

        foreach (var key in keys)
            _memoryCache.Remove(key);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Releases all cache entries and user scopes.
    /// </summary>
    public void Dispose()
    {
        foreach (var pair in _userScopes)
        {
            pair.Value.Retire();
            RemoveScope(pair.Key, pair.Value);
        }

        _memoryCache.Dispose();
    }

    private bool TryGetValue<T>(
        ScopedCacheKey scopedKey,
        IReadOnlyCollection<string> dependencyTags,
        out T? value)
    {
        if (_memoryCache.TryGetValue(scopedKey, out CachedValue<T>? cachedValue) &&
            cachedValue is not null &&
            cachedValue.Registration.Scope.IsCurrent(cachedValue.Registration) &&
            cachedValue.Registration.HasTags(dependencyTags))
        {
            value = cachedValue.Value;
            return true;
        }

        _memoryCache.Remove(scopedKey);
        value = default;
        return false;
    }

    private void ReleaseEntry(CacheEntryRegistration registration)
    {
        registration.Scope.Remove(registration);
        RemoveScopeIfIdle(registration.Key.UserId, registration.Scope);
    }

    private void RemoveScopeIfIdle(string userId, UserCacheScope scope)
    {
        if (scope.TryRetireIfIdle())
            RemoveScope(userId, scope);
    }

    private void RemoveScope(string userId, UserCacheScope scope)
    {
        var scopes = (ICollection<KeyValuePair<string, UserCacheScope>>)_userScopes;
        scopes.Remove(new KeyValuePair<string, UserCacheScope>(userId, scope));
    }

    private async Task<FactoryLockLease> AcquireFactoryLockAsync(
        ScopedCacheKey key,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var factoryLock = _factoryLocks.GetOrAdd(key, static _ => new FactoryLock());
            lock (factoryLock.Gate)
            {
                if (factoryLock.IsRemoved)
                    continue;

                factoryLock.ReferenceCount++;
            }

            try
            {
                await factoryLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new FactoryLockLease(this, key, factoryLock);
            }
            catch
            {
                ReleaseFactoryLock(key, factoryLock, releaseSemaphore: false);
                throw;
            }
        }
    }

    private void ReleaseFactoryLock(
        ScopedCacheKey key,
        FactoryLock factoryLock,
        bool releaseSemaphore)
    {
        if (releaseSemaphore)
            factoryLock.Semaphore.Release();

        lock (factoryLock.Gate)
        {
            factoryLock.ReferenceCount--;
            if (factoryLock.ReferenceCount != 0)
                return;

            factoryLock.IsRemoved = true;
            var locks = (ICollection<KeyValuePair<ScopedCacheKey, FactoryLock>>)_factoryLocks;
            locks.Remove(new KeyValuePair<ScopedCacheKey, FactoryLock>(key, factoryLock));
        }

        factoryLock.Semaphore.Dispose();
    }

    private static string[] NormalizeTags(IReadOnlyCollection<string> dependencyTags)
    {
        ArgumentNullException.ThrowIfNull(dependencyTags);
        if (dependencyTags.Count == 0)
            throw new ArgumentException("At least one dependency tag is required.", nameof(dependencyTags));

        var normalizedTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in dependencyTags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("Dependency tags cannot be empty.", nameof(dependencyTags));

            normalizedTags.Add(tag);
        }

        return [.. normalizedTags];
    }

    private readonly record struct ScopedCacheKey(
        string UserId,
        string LogicalKey,
        Type ValueType);

    private sealed record CachedValue<T>(T Value, CacheEntryRegistration Registration);

    private sealed class FactoryLock
    {
        public object Gate { get; } = new();

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }

        public bool IsRemoved { get; set; }
    }

    private sealed class FactoryLockLease(
        MemoryApplicationCache owner,
        ScopedCacheKey key,
        FactoryLock factoryLock) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ReleaseFactoryLock(key, factoryLock, releaseSemaphore: true);
        }
    }

    private sealed class CacheEntryRegistration(
        UserCacheScope scope,
        ScopedCacheKey key,
        string[] dependencyTags)
    {
        public UserCacheScope Scope { get; } = scope;

        public ScopedCacheKey Key { get; } = key;

        public IReadOnlyList<string> DependencyTags { get; } = dependencyTags;

        public bool HasTags(IReadOnlyCollection<string> tags)
        {
            if (DependencyTags.Count != tags.Count)
                return false;

            foreach (var tag in tags)
            {
                if (!DependencyTags.Contains(tag, StringComparer.Ordinal))
                    return false;
            }

            return true;
        }
    }

    private sealed class UserCacheScope
    {
        private readonly object _gate = new();
        private readonly Dictionary<ScopedCacheKey, CacheEntryRegistration> _entries = [];
        private readonly Dictionary<string, HashSet<CacheEntryRegistration>> _entriesByTag =
            new(StringComparer.Ordinal);
        private long _generation;
        private int _activeFactories;
        private bool _retired;

        public bool TryBeginFactory(out long generation)
        {
            lock (_gate)
            {
                generation = _generation;
                if (_retired)
                    return false;

                _activeFactories++;
                return true;
            }
        }

        public void EndFactory()
        {
            lock (_gate)
            {
                _activeFactories--;
            }
        }

        public bool TryPublish(
            long generation,
            CacheEntryRegistration registration,
            Action publish)
        {
            lock (_gate)
            {
                if (_retired || generation != _generation)
                    return false;

                if (_entries.TryGetValue(registration.Key, out var previous))
                    RemoveCore(previous);

                AddCore(registration);
                try
                {
                    publish();
                    return true;
                }
                catch
                {
                    RemoveCore(registration);
                    throw;
                }
            }
        }

        public bool IsCurrent(CacheEntryRegistration registration)
        {
            lock (_gate)
            {
                return !_retired &&
                       _entries.TryGetValue(registration.Key, out var current) &&
                       ReferenceEquals(current, registration);
            }
        }

        public IReadOnlyCollection<ScopedCacheKey> InvalidateTags(IEnumerable<string> tags)
        {
            lock (_gate)
            {
                if (_retired)
                    return [];

                _generation++;
                var registrations = new HashSet<CacheEntryRegistration>();
                foreach (var tag in tags)
                {
                    if (_entriesByTag.TryGetValue(tag, out var taggedEntries))
                        registrations.UnionWith(taggedEntries);
                }

                var keys = new ScopedCacheKey[registrations.Count];
                var index = 0;
                foreach (var registration in registrations)
                {
                    keys[index++] = registration.Key;
                    RemoveCore(registration);
                }

                return keys;
            }
        }

        public IReadOnlyCollection<ScopedCacheKey> Retire()
        {
            lock (_gate)
            {
                if (_retired)
                    return [];

                _retired = true;
                _generation++;
                var keys = _entries.Keys.ToArray();
                _entries.Clear();
                _entriesByTag.Clear();
                return keys;
            }
        }

        public void Remove(CacheEntryRegistration registration)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(registration.Key, out var current) &&
                    ReferenceEquals(current, registration))
                {
                    RemoveCore(registration);
                }
            }
        }

        public bool TryRetireIfIdle()
        {
            lock (_gate)
            {
                if (_retired || _activeFactories != 0 || _entries.Count != 0)
                    return false;

                _retired = true;
                return true;
            }
        }

        private void AddCore(CacheEntryRegistration registration)
        {
            _entries.Add(registration.Key, registration);
            foreach (var tag in registration.DependencyTags)
            {
                if (!_entriesByTag.TryGetValue(tag, out var taggedEntries))
                {
                    taggedEntries = [];
                    _entriesByTag.Add(tag, taggedEntries);
                }

                taggedEntries.Add(registration);
            }
        }

        private void RemoveCore(CacheEntryRegistration registration)
        {
            if (!_entries.Remove(registration.Key))
                return;

            foreach (var tag in registration.DependencyTags)
            {
                if (!_entriesByTag.TryGetValue(tag, out var taggedEntries))
                    continue;

                taggedEntries.Remove(registration);
                if (taggedEntries.Count == 0)
                    _entriesByTag.Remove(tag);
            }
        }
    }
}
