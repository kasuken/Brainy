using Brainy.Application.Common;
using Brainy.Application.Caching;
using Brainy.Application.DTOs.Resources;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Common;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Resource"/> entities, scoped to the current user.
/// Active resources exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class ResourceService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IResourceService
{
    public async Task<IReadOnlyList<ResourceDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "resources:active",
            [
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTypeTag<Tag>()
            ],
            async ct => await context.Resources
                .AsNoTracking()
                .Include(r => r.Tags)
                .Where(r => r.UserId == userId && !r.IsArchived)
                .OrderBy(r => r.Name)
                .Select(r => ToDto(r))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResourceDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "resources:archived",
            [
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTypeTag<Tag>()
            ],
            async ct => await context.Resources
                .AsNoTracking()
                .Include(r => r.Tags)
                .Where(r => r.UserId == userId && r.IsArchived)
                .OrderByDescending(r => r.ArchivedAtUtc)
                .Select(r => ToDto(r))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResourceDto>> SearchAsync(
        string? searchText,
        string? topic,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            ApplicationCacheKey.Create("resources", "search", searchText, topic),
            [
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTypeTag<Tag>()
            ],
            ct => SearchCoreAsync(userId, searchText, topic, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ResourceDto>> SearchCoreAsync(
        string userId,
        string? searchText,
        string? topic,
        CancellationToken cancellationToken)
    {
        var query = context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .Where(r => r.UserId == userId && !r.IsArchived);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(r =>
                r.Name.Contains(searchText) ||
                (r.Description != null && r.Description.Contains(searchText)) ||
                (r.Topic != null && r.Topic.Contains(searchText)));
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            query = query.Where(r => r.Topic != null && r.Topic == topic);
        }

        return await query
            .OrderBy(r => r.Name)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ResourceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"resources:{id}:summary",
            [
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTag<Resource>(id),
                ApplicationCacheKey.EntityTypeTag<Tag>()
            ],
            ct => context.Resources
                .AsNoTracking()
                .Include(r => r.Tags)
                .Where(r => r.Id == id && r.UserId == userId)
                .Select(r => ToDto(r))
                .FirstOrDefaultAsync(ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResourceDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"resources:{id}:detail",
            [
                ApplicationCacheKey.EntityTypeTag<Resource>(),
                ApplicationCacheKey.EntityTag<Resource>(id),
                ApplicationCacheKey.EntityTypeTag<Tag>(),
                ApplicationCacheKey.EntityTypeTag<Note>()
            ],
            ct => GetDetailCoreAsync(id, userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResourceDetailDto?> GetDetailCoreAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken)
    {
        var resource = await context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (resource is null)
            return null;

        var notes = await context.Notes
            .AsNoTracking()
            .Where(n => n.ResourceId == id && n.UserId == userId)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new ResourceNoteDto(n.Id, n.Title, n.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ResourceDetailDto(
            resource.Id,
            resource.Name,
            resource.Description,
            resource.Topic,
            resource.IsArchived,
            resource.ArchivedAtUtc,
            resource.AreaId,
            resource.CreatedAtUtc,
            resource.UpdatedAtUtc,
            resource.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
            notes.Count,
            notes,
            NormalizeEmoji(resource.Emoji),
            resource.RowVersion,
            resource.ArchivedReason);
    }

    public async Task<ResourceDto> CreateAsync(CreateResourceDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Emoji = NormalizeEmoji(dto.Emoji),
            Description = dto.Description,
            Topic = dto.Topic,
            AreaId = dto.AreaId
        };

        if (dto.Tags is { Count: > 0 })
        {
            resource.Tags = await ResolveTagsAsync(userId, dto.Tags, cancellationToken).ConfigureAwait(false);
        }

        context.Resources.Add(resource);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateResourceAsync(userId, resource, includeTags: true).ConfigureAwait(false);

        return ToDto(resource);
    }

    public async Task<ResourceDto> UpdateAsync(UpdateResourceDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == dto.Id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{dto.Id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(dto.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        if (dto.RowVersion is not null)
            context.Entry(resource).Property(r => r.RowVersion).OriginalValue = dto.RowVersion;

        resource.Name = dto.Name;
        resource.Emoji = NormalizeEmoji(dto.Emoji);
        resource.Description = dto.Description;
        resource.Topic = dto.Topic;
        resource.AreaId = dto.AreaId;

        // Replace tag collection; resolve names to existing or new Tag rows.
        resource.Tags = dto.Tags is { Count: > 0 }
            ? await ResolveTagsAsync(userId, dto.Tags, cancellationToken).ConfigureAwait(false)
            : new List<Tag>();

        // Force one Resource UPDATE even when only its tag join rows changed so the
        // rowversion predicate is checked and SQL Server advances the token.
        context.Entry(resource).Property(r => r.UpdatedAtUtc).IsModified = true;

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("resource", ex);
        }

        await InvalidateResourceAsync(userId, resource, includeTags: true).ConfigureAwait(false);
        return ToDto(resource);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        var normalizedReason = ArchiveReasonNormalizer.Normalize(archivedReason);
        resource.IsArchived = true;
        resource.ArchivedAtUtc = DateTime.UtcNow;
        resource.ArchivedReason = normalizedReason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateResourceAsync(userId, resource).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        await context.Areas.EnsureActiveOwnedAreaAsync(resource.AreaId, userId, cancellationToken)
            .ConfigureAwait(false);

        resource.IsArchived = false;
        resource.ArchivedAtUtc = null;
        resource.ArchivedReason = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateResourceAsync(userId, resource).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        Guid id,
        byte[]? rowVersion,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        if (rowVersion is not null)
            context.Entry(resource).Property(r => r.RowVersion).OriginalValue = rowVersion;

        context.Resources.Remove(resource);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("resource", ex);
        }
        await InvalidateResourceAsync(userId, resource, includeNotes: true).ConfigureAwait(false);
    }

    public async Task LinkNoteAsync(Guid resourceId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Verify the resource belongs to the user.
        var resourceExists = await context.Resources
            .AnyAsync(r => r.Id == resourceId && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!resourceExists)
            throw new KeyNotFoundException($"Resource '{resourceId}' was not found.");

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ResourceId = resourceId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
    }

    public async Task UnlinkNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.ResourceId = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateNoteAsync(userId, note.Id).ConfigureAwait(false);
    }

    private ValueTask InvalidateResourceAsync(
        string userId,
        Resource resource,
        bool includeTags = false,
        bool includeNotes = false)
    {
        List<string> tags =
        [
            ApplicationCacheKey.EntityTypeTag<Resource>(),
            ApplicationCacheKey.EntityTag<Resource>(resource.Id)
        ];

        if (includeTags)
        {
            tags.Add(ApplicationCacheKey.EntityTypeTag<Tag>());
            tags.AddRange(resource.Tags.Select(tag => ApplicationCacheKey.EntityTag<Tag>(tag.Id)));
        }

        if (includeNotes)
            tags.Add(ApplicationCacheKey.EntityTypeTag<Note>());

        return cache.InvalidateTagsAsync(userId, tags, CancellationToken.None);
    }

    private ValueTask InvalidateNoteAsync(string userId, Guid noteId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<Note>(),
                ApplicationCacheKey.EntityTag<Note>(noteId)
            ],
            CancellationToken.None);

    /// <summary>
    /// Resolves tag names to existing <see cref="Tag"/> rows for the given user,
    /// creating any that don't already exist. Comparison is case-insensitive.
    /// </summary>
    private async Task<List<Tag>> ResolveTagsAsync(
        string userId,
        IReadOnlyList<string> tagNames,
        CancellationToken cancellationToken)
    {
        // Normalise to distinct, non-empty names.
        var names = tagNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
            return [];

        var lowerNames = names.Select(n => n.ToLowerInvariant()).ToList();

        var existing = await context.Tags
            .Where(t => t.UserId == userId && lowerNames.Contains(t.Name.ToLower()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByName = existing
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<Tag>(names.Count);

        foreach (var name in names)
        {
            if (existingByName.TryGetValue(name, out var tag))
            {
                resolved.Add(tag);
            }
            else
            {
                var newTag = new Tag { Id = Guid.NewGuid(), UserId = userId, Name = name };
                context.Tags.Add(newTag);
                resolved.Add(newTag);
            }
        }

        return resolved;
    }

    private static ResourceDto ToDto(Resource r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.Topic,
        r.IsArchived,
        r.ArchivedAtUtc,
        r.AreaId,
        r.CreatedAtUtc,
        r.UpdatedAtUtc,
        r.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
        NormalizeEmoji(r.Emoji),
        r.RowVersion,
        r.ArchivedReason);

    private static string NormalizeEmoji(string? emoji)
    {
        var normalized = string.IsNullOrWhiteSpace(emoji)
            ? ResourceEmojiDefaults.DefaultEmoji
            : emoji.Trim();

        if (normalized.Length > ResourceEmojiDefaults.MaxLength)
            throw new ArgumentException($"Resource emoji cannot exceed {ResourceEmojiDefaults.MaxLength} characters.", nameof(emoji));

        return normalized;
    }
}
