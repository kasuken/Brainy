using Brainy.Application.Caching;
using Brainy.Application.DTOs.Tags;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="Tag"/> lifecycle for the current user: usage stats for the tag
/// management page, rename, merge, delete, and bulk removal of unused tags.
/// Tags are shared between <see cref="Note"/> and <see cref="Resource"/>, and also feed
/// <see cref="ISearchService"/> matching and filter chips, so every mutation here invalidates
/// the shared <see cref="Tag"/> cache tag rather than only a private "tags" key.
/// </summary>
internal sealed class TagService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : ITagService
{
    private const int MaxNameLength = 100;

    public async Task<IReadOnlyList<TagDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "tags:all",
            [
                // Renaming/merging/deleting a tag invalidates this directly, and any Note or
                // Resource mutation also invalidates its own entity-type tag below — both are
                // needed since a note/resource archive or delete changes usage counts without
                // touching the Tag row itself.
                ApplicationCacheKey.EntityTypeTag<Tag>(),
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTypeTag<Resource>()
            ],
            async ct =>
            {
                var raw = await context.Tags.AsNoTracking()
                    .Where(t => t.UserId == userId)
                    .Select(t => new
                    {
                        t.Id,
                        t.Name,
                        t.Color,
                        t.CreatedAtUtc,
                        NoteCount = t.Notes.Count(n => n.UserId == userId && !n.IsArchived),
                        ResourceCount = t.Resources.Count(r => r.UserId == userId && !r.IsArchived),
                        LastNoteUsedAtUtc = t.Notes
                            .Where(n => n.UserId == userId && !n.IsArchived)
                            .Select(n => (DateTime?)n.UpdatedAtUtc)
                            .Max(),
                        LastResourceUsedAtUtc = t.Resources
                            .Where(r => r.UserId == userId && !r.IsArchived)
                            .Select(r => (DateTime?)r.UpdatedAtUtc)
                            .Max()
                    })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return (IReadOnlyList<TagDto>)raw
                    .Select(t => new TagDto(
                        t.Id,
                        t.Name,
                        t.Color,
                        t.NoteCount,
                        t.ResourceCount,
                        LatestOf(t.LastNoteUsedAtUtc, t.LastResourceUsedAtUtc),
                        t.CreatedAtUtc))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameAsync(Guid id, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var trimmed = newName.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new ArgumentException($"Tag names cannot exceed {MaxNameLength} characters.", nameof(newName));
        if (trimmed.Any(char.IsControl))
            throw new ArgumentException("Tag names cannot contain control characters.", nameof(newName));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tag '{id}' was not found.");

        if (string.Equals(tag.Name, trimmed, StringComparison.Ordinal))
            return;

        var conflict = await context.Tags.AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.Id != id && t.Name.ToLower() == trimmed.ToLower(), cancellationToken)
            .ConfigureAwait(false);
        if (conflict)
            throw new InvalidOperationException(
                $"A tag named \"{trimmed}\" already exists. Merge the tags instead of renaming.");

        tag.Name = trimmed;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateTagAsync(userId, tag.Id).ConfigureAwait(false);
    }

    public async Task MergeAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
            throw new ArgumentException("Cannot merge a tag into itself.", nameof(targetId));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var source = await context.Tags
            .Include(t => t.Notes)
            .Include(t => t.Resources)
            .FirstOrDefaultAsync(t => t.Id == sourceId && t.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tag '{sourceId}' was not found.");

        var target = await context.Tags
            .Include(t => t.Notes)
            .Include(t => t.Resources)
            .FirstOrDefaultAsync(t => t.Id == targetId && t.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tag '{targetId}' was not found.");

        // Reassign every association from source to target, skipping notes/resources already
        // tagged with the target so the merge never tries to insert a duplicate join row.
        foreach (var note in source.Notes.ToList())
        {
            if (target.Notes.All(n => n.Id != note.Id))
                target.Notes.Add(note);
            source.Notes.Remove(note);
        }

        foreach (var resource in source.Resources.ToList())
        {
            if (target.Resources.All(r => r.Id != resource.Id))
                target.Resources.Add(resource);
            source.Resources.Remove(resource);
        }

        context.Tags.Remove(source);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Tag>(),
                ApplicationCacheKey.EntityTag<Tag>(sourceId),
                ApplicationCacheKey.EntityTag<Tag>(targetId)
            ],
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tag '{id}' was not found.");

        // Removing the Tag row cascades the NoteTag/ResourceTag join rows only; notes and
        // resources themselves are untouched.
        context.Tags.Remove(tag);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateTagAsync(userId, tag.Id).ConfigureAwait(false);
    }

    public async Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var unused = await context.Tags
            .Where(t => t.UserId == userId
                     && !t.Notes.Any(n => n.UserId == userId && !n.IsArchived)
                     && !t.Resources.Any(r => r.UserId == userId && !r.IsArchived))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (unused.Count == 0)
            return 0;

        context.Tags.RemoveRange(unused);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var tags = new List<string> { ApplicationCacheKey.EntityTypeTag<Tag>() };
        tags.AddRange(unused.Select(t => ApplicationCacheKey.EntityTag<Tag>(t.Id)));
        await cache.InvalidateTagsAsync(userId, tags, CancellationToken.None).ConfigureAwait(false);

        return unused.Count;
    }

    private ValueTask InvalidateTagAsync(string userId, Guid tagId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Tag>(),
                ApplicationCacheKey.EntityTag<Tag>(tagId)
            ],
            CancellationToken.None);

    private static DateTime? LatestOf(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }
}
