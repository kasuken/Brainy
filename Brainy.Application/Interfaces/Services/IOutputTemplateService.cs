using Brainy.Application.DTOs.Outputs;
using Brainy.Application.DTOs.Templates;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages reusable <see cref="Domain.Entities.OutputTemplate"/> blueprints and
/// instantiates them into real outputs through the normal <see cref="IOutputService"/>
/// creation path.
/// </summary>
public interface IOutputTemplateService
{
    /// <summary>
    /// Every template owned by the current user, seeding the built-in starter set
    /// first if the user has never had any output templates.
    /// </summary>
    Task<IReadOnlyList<OutputTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<OutputTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OutputTemplateDto> CreateAsync(CreateOutputTemplateDto dto, CancellationToken cancellationToken = default);

    Task<OutputTemplateDto> UpdateAsync(UpdateOutputTemplateDto dto, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Captures an existing output's title, type and content as a new template.</summary>
    Task<OutputTemplateDto> CreateFromOutputAsync(SaveOutputAsTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new output from a template. A plain create through
    /// <see cref="IOutputService.CreateAsync"/> — no hidden linkage back to the
    /// template. When the template's source-selection rule applies and a project is
    /// supplied, that project's active notes are resolved into plain source-note ids.
    /// </summary>
    Task<OutputDto> InstantiateAsync(InstantiateOutputTemplateDto dto, CancellationToken cancellationToken = default);
}
