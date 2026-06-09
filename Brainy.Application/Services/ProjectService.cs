using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Project"/> entities.
/// Active projects exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class ProjectService(IApplicationDbContext context) : IProjectService
{
    public async Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .OrderByDescending(p => p.IsPriority)
            .ThenBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return project is null ? null : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            DueDate = dto.DueDate,
            IsPriority = dto.IsPriority,
            AreaId = dto.AreaId
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        project.IsArchived = true;
        project.ArchivedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.DueDate,
        p.IsArchived,
        p.IsPriority,
        p.AreaId,
        p.CreatedAtUtc,
        p.UpdatedAtUtc);
}
