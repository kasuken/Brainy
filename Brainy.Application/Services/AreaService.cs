using Brainy.Application.DTOs.Areas;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
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
        return await context.Areas.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AreaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await context.Areas.AsNoTracking()
            .Where(a => a.UserId == userId && a.IsArchived)
            .OrderByDescending(a => a.ArchivedAtUtc)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AreaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var area = await context.Areas.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false);
        return area is null ? null : ToDto(area);
    }

    public async Task<AreaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false);

        if (area is null) return null;

        var activeProjectCount = await context.Projects.AsNoTracking()
            .CountAsync(p => p.AreaId == id && !p.IsArchived, cancellationToken).ConfigureAwait(false);

        var openTaskCount = await context.Tasks.AsNoTracking()
            .CountAsync(t => t.Project.AreaId == id && !t.IsArchived && !t.Project.IsArchived
                          && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Archived, cancellationToken)
            .ConfigureAwait(false);

        var recentNoteCount = await context.Notes.AsNoTracking()
            .CountAsync(n => n.AreaId == id && n.UserId == userId, cancellationToken).ConfigureAwait(false);

        return new AreaDetailDto(area.Id, area.Name, area.Description, area.Purpose,
            area.IsArchived, area.ArchivedAtUtc, area.CreatedAtUtc, area.UpdatedAtUtc,
            activeProjectCount, openTaskCount, recentNoteCount);
    }

    public async Task<AreaDto> CreateAsync(CreateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Area name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = new Area
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Purpose = dto.Purpose?.Trim()
        };

        context.Areas.Add(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(area);
    }

    public async Task<AreaDto> UpdateAsync(UpdateAreaDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Area name is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == dto.Id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{dto.Id}' was not found.");

        area.Name = dto.Name.Trim();
        area.Description = dto.Description?.Trim();
        area.Purpose = dto.Purpose?.Trim();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(area);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        area.IsArchived = true;
        area.ArchivedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        area.IsArchived = false;
        area.ArchivedAtUtc = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var area = await context.Areas
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Area '{id}' was not found.");

        context.Areas.Remove(area);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LinkProjectAsync(Guid areaId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var areaExists = await context.Areas
            .AnyAsync(a => a.Id == areaId && a.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (!areaExists)
            throw new KeyNotFoundException($"Area '{areaId}' was not found.");

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        project.AreaId = areaId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlinkProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        project.AreaId = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LinkNoteAsync(Guid areaId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var areaExists = await context.Areas
            .AnyAsync(a => a.Id == areaId && a.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (!areaExists)
            throw new KeyNotFoundException($"Area '{areaId}' was not found.");

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.AreaId = areaId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlinkNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var note = await context.Notes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        note.AreaId = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AreaDto ToDto(Area a) => new(
        a.Id, a.Name, a.Description, a.Purpose,
        a.IsArchived, a.ArchivedAtUtc,
        a.CreatedAtUtc, a.UpdatedAtUtc);
}
