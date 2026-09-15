using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Templates;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages reusable <see cref="Domain.Entities.ProjectTemplate"/> blueprints and
/// instantiates them into real projects. Templates themselves count against no
/// entitlement limit; <see cref="InstantiateAsync"/> creates a project through the
/// normal <see cref="IProjectService"/> path, so the usual active-project limit
/// applies there exactly as it would for a hand-made project.
/// </summary>
public interface IProjectTemplateService
{
    /// <summary>
    /// Every template owned by the current user, seeding the built-in starter set
    /// first if the user has never had any project templates.
    /// </summary>
    Task<IReadOnlyList<ProjectTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProjectTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectTemplateDto> CreateAsync(CreateProjectTemplateDto dto, CancellationToken cancellationToken = default);

    Task<ProjectTemplateDto> UpdateAsync(UpdateProjectTemplateDto dto, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures an existing project's name, description, default area/goal and current
    /// non-archived top-level tasks as a new template. Each task's due date is stored
    /// as an offset in days from today, so instantiating the template later recreates
    /// the same relative schedule.
    /// </summary>
    Task<ProjectTemplateDto> CreateFromProjectAsync(SaveProjectAsTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new project and its default task list from a template. A plain create
    /// through <see cref="IProjectService.CreateAsync"/> and <see cref="ITaskService.CreateAsync"/> —
    /// no hidden linkage back to the template. Throws
    /// <see cref="Common.PlanEntitlementDeniedException"/> when the user is at their
    /// plan's active-project limit, exactly as a hand-made project creation would.
    /// </summary>
    Task<ProjectDto> InstantiateAsync(InstantiateProjectTemplateDto dto, CancellationToken cancellationToken = default);
}
