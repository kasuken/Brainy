using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="Project"/> entities, scoped to the current user.
/// Active projects exclude archived entries; reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class ProjectService(IApplicationDbContext context, ICurrentUserService currentUser) : IProjectService
{
    public async Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == ProjectStatus.Active)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status != ProjectStatus.Archived && !p.IsArchived)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Status)
            .ThenBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && (p.IsArchived || p.Status == ProjectStatus.Archived))
            .OrderByDescending(p => p.ArchivedAtUtc)
            .ThenBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetProjectSummariesAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var data = await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsArchived && p.Status != ProjectStatus.Archived)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.DesiredOutcome, p.Status, p.Priority,
                p.StartDate, p.DueDate, p.CompletedDate, p.IsArchived, p.AreaId,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.ArchivedAtUtc,
                TotalTasks = p.Tasks.Count(t => !t.IsArchived),
                OpenTasks  = p.Tasks.Count(t => !t.IsArchived && t.Status != TaskItemStatus.Done),
                DoneTasks  = p.Tasks.Count(t => !t.IsArchived && t.Status == TaskItemStatus.Done),
            })
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return data.Select(x => new ProjectSummaryDto(
            x.Id, x.Name, x.Description, x.DesiredOutcome, x.Status, x.Priority,
            x.StartDate, x.DueDate, x.CompletedDate, x.IsArchived, x.AreaId,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.ArchivedAtUtc,
            x.TotalTasks, x.OpenTasks, x.DoneTasks,
            x.TotalTasks > 0 ? Math.Round((double)x.DoneTasks / x.TotalTasks * 100, 1) : 0))
            .ToList();
    }

    public async Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return project is null ? null : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            DesiredOutcome = dto.DesiredOutcome,
            Status = dto.Status,
            Priority = dto.Priority,
            StartDate = dto.StartDate,
            DueDate = dto.DueDate,
            AreaId = dto.AreaId
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task<ProjectDto> UpdateAsync(UpdateProjectDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == dto.Id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{dto.Id}' was not found.");

        project.Name = dto.Name;
        project.Description = dto.Description;
        project.DesiredOutcome = dto.DesiredOutcome;
        project.Status = dto.Status;
        project.Priority = dto.Priority;
        project.StartDate = dto.StartDate;
        project.DueDate = dto.DueDate;
        project.AreaId = dto.AreaId;

        if (dto.Status == ProjectStatus.Completed && project.CompletedDate is null)
            project.CompletedDate = DateTime.UtcNow;
        else if (dto.Status != ProjectStatus.Completed)
            project.CompletedDate = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        project.IsArchived = true;
        project.ArchivedAtUtc = DateTime.UtcNow;
        project.Status = ProjectStatus.Archived;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        project.IsArchived = false;
        project.ArchivedAtUtc = null;
        project.Status = ProjectStatus.NotStarted;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var project = await context.Projects
            .Include(p => p.Tasks)
            .Include(p => p.Notes)
            .Include(p => p.Outputs)
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project '{id}' was not found.");

        if (project.Tasks.Count > 0 || project.Notes.Count > 0 || project.Outputs.Count > 0)
            throw new InvalidOperationException(
                $"Project '{project.Name}' cannot be deleted because it still has " +
                $"{project.Tasks.Count} task(s), {project.Notes.Count} note(s), and " +
                $"{project.Outputs.Count} output(s). Remove or reassign them first.");

        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.DesiredOutcome,
        p.Status,
        p.Priority,
        p.StartDate,
        p.DueDate,
        p.CompletedDate,
        p.IsArchived,
        p.AreaId,
        p.CreatedAtUtc,
        p.UpdatedAtUtc,
        p.ArchivedAtUtc);
}
