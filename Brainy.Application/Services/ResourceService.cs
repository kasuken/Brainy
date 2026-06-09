using Brainy.Application.DTOs.Resources;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
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
            .Where(r => r.UserId == userId && !r.IsArchived)
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
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return resource is null ? null : ToDto(resource);
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
            Description = dto.Description,
            AreaId = dto.AreaId
        };

        context.Resources.Add(resource);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(resource);
    }

    public async Task<ResourceDto> UpdateAsync(UpdateResourceDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var resource = await context.Resources
            .FirstOrDefaultAsync(r => r.Id == dto.Id && r.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Resource '{dto.Id}' was not found.");

        resource.Name = dto.Name;
        resource.Description = dto.Description;
        resource.AreaId = dto.AreaId;

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

    private static ResourceDto ToDto(Resource r) => new(
        r.Id,
        r.Name,
        r.Description,
        r.IsArchived,
        r.AreaId,
        r.CreatedAtUtc,
        r.UpdatedAtUtc);
}
