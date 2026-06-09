using Brainy.Application.DTOs.Areas;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Area"/> entities, scoped to the current user.
/// Active areas exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class AreaService(IApplicationDbContext context, ICurrentUserService currentUser) : IAreaService
{
    public async Task<IReadOnlyList<AreaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Areas
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AreaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return area is null ? null : ToDto(area);
    }

    public async Task<AreaDto> CreateAsync(CreateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = new Area
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description
        };

        context.Areas.Add(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(area);
    }

    public async Task<AreaDto> UpdateAsync(UpdateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == dto.Id && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{dto.Id}' was not found.");

        area.Name = dto.Name;
        area.Description = dto.Description;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(area);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        area.IsArchived = true;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        context.Areas.Remove(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AreaDto ToDto(Area a) => new(
        a.Id,
        a.Name,
        a.Description,
        a.IsArchived,
        a.CreatedAtUtc,
        a.UpdatedAtUtc);
}
