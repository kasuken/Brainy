using Brainy.Application.DTOs.Notes;
using Brainy.Application.DTOs.Templates;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages reusable <see cref="Domain.Entities.NoteTemplate"/> blueprints and
/// instantiates them into real notes through the normal <see cref="INoteService"/>
/// creation path (so an initial <see cref="Domain.Entities.NoteRevision"/> is still
/// recorded, exactly as for a hand-made note).
/// </summary>
public interface INoteTemplateService
{
    /// <summary>
    /// Every template owned by the current user, seeding the built-in starter set
    /// first if the user has never had any note templates.
    /// </summary>
    Task<IReadOnlyList<NoteTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<NoteTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NoteTemplateDto> CreateAsync(CreateNoteTemplateDto dto, CancellationToken cancellationToken = default);

    Task<NoteTemplateDto> UpdateAsync(UpdateNoteTemplateDto dto, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Captures an existing note's title and content as a new template.</summary>
    Task<NoteTemplateDto> CreateFromNoteAsync(SaveNoteAsTemplateDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new note from a template. A plain create through
    /// <see cref="INoteService.CreateAsync"/> — no hidden linkage back to the template.
    /// </summary>
    Task<NoteDto> InstantiateAsync(InstantiateNoteTemplateDto dto, CancellationToken cancellationToken = default);
}
