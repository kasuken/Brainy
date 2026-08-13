using Brainy.Application.Interfaces.Persistence;
using Brainy.Domain.Common;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Common;

/// <summary>
/// Validates that related entities selected by a caller belong to the current user.
/// Keeping this check at the application boundary prevents valid foreign keys from
/// creating cross-user relationships.
/// </summary>
internal static class UserOwnedReferenceValidationExtensions
{
    public static async Task EnsureOwnedAsync<TEntity>(
        this IQueryable<TEntity> entities,
        Guid? id,
        string userId,
        string entityName,
        CancellationToken cancellationToken)
        where TEntity : BaseEntity, IUserOwnedEntity
    {
        if (!id.HasValue)
            return;

        var isOwned = await entities
            .AsNoTracking()
            .AnyAsync(entity => entity.Id == id.Value && entity.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!isOwned)
            throw new KeyNotFoundException($"{entityName} '{id.Value}' was not found.");
    }

    public static async Task EnsureNoteLinksOwnedAsync(
        this IApplicationDbContext context,
        string userId,
        Guid? projectId,
        Guid? areaId,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        await context.Projects.EnsureOwnedAsync(projectId, userId, "Project", cancellationToken)
            .ConfigureAwait(false);
        await context.Areas.EnsureActiveOwnedAreaAsync(areaId, userId, cancellationToken)
            .ConfigureAwait(false);
        await context.Resources.EnsureOwnedAsync(resourceId, userId, "Resource", cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task EnsureActiveOwnedAreaAsync(
        this IQueryable<Area> areas,
        Guid? areaId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!areaId.HasValue)
            return;

        var area = await areas
            .AsNoTracking()
            .Where(a => a.Id == areaId.Value && a.UserId == userId)
            .Select(a => new { a.IsArchived })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{areaId.Value}' was not found.");

        if (area.IsArchived)
            throw new InvalidOperationException("Active work cannot be assigned to an archived area. Restore the area first.");
    }
}
