using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A reusable blueprint for creating a <see cref="Note"/>: a title pattern and a
/// content scaffold. Instantiating a note template is a plain <see cref="Note"/>
/// create through the normal note creation service.
/// </summary>
public class NoteTemplate : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name of the template itself (shown in the template picker).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Default title given to the note created from this template. May contain the
    /// tokens <c>{Date}</c> and <c>{Year}</c>, resolved against the user's calendar
    /// date at instantiation time.
    /// </summary>
    public string TitlePattern { get; set; } = string.Empty;

    /// <summary>Default body content — the scaffold the user fills in.</summary>
    public string ContentScaffold { get; set; } = string.Empty;

    public ParaCategory DefaultParaCategory { get; set; } = ParaCategory.Project;

    /// <summary>
    /// True for the small starter set seeded automatically for every user so the
    /// feature is useful on day one.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}
