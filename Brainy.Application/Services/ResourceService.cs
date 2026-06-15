using Brainy.Application.DTOs.Resources;
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
internal sealed class ResourceService(IApplicationDbContext context, ICurrentUserService currentUser) : IResourceService
{
    public async Task<IReadOnlyList<ResourceDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .Where(r => r.UserId == userId && !r.IsArchived)
            .OrderBy(r => r.Name)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResourceDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .Where(r => r.UserId == userId && r.IsArchived)
            .OrderByDescending(r => r.ArchivedAtUtc)
            .Select(r => ToDto(r))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResourceDto>> SearchAsync(
        string? searchText,
        string? topic,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

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

        var resource = await context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return resource is null ? null : ToDto(resource);
    }

    public async Task<ResourceDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .AsNoTracking()
            .Include(r => r.Tags)
            .Include(r => r.Notes)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (resource is null)
            return null;

        var notes = resource.Notes
            .OrderByDescending(n => n.UpdatedAtUtc)
            .Select(n => new ResourceNoteDto(n.Id, n.Title, n.UpdatedAtUtc))
            .ToList();

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
            NormalizeEmoji(resource.Emoji));
    }

    public async Task<ResourceDto> CreateAsync(CreateResourceDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

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

        resource.Name = dto.Name;
        resource.Emoji = NormalizeEmoji(dto.Emoji);
        resource.Description = dto.Description;
        resource.Topic = dto.Topic;
        resource.AreaId = dto.AreaId;

        // Replace tag collection; resolve names to existing or new Tag rows.
        resource.Tags = dto.Tags is { Count: > 0 }
            ? await ResolveTagsAsync(userId, dto.Tags, cancellationToken).ConfigureAwait(false)
            : new List<Tag>();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(resource);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        resource.IsArchived = true;
        resource.ArchivedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        resource.IsArchived = false;
        resource.ArchivedAtUtc = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{id}' was not found.");

        context.Resources.Remove(resource);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
    }

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
        NormalizeEmoji(r.Emoji));

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
