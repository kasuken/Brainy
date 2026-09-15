using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A reusable blueprint for creating an <see cref="Output"/>: its type, a content
/// structure, and a rule for pre-selecting source notes. Instantiating an output
/// template is a plain <see cref="Output"/> create through the normal output creation
/// service.
/// </summary>
public class OutputTemplate : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name of the template itself (shown in the template picker).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Default title given to the output created from this template. May contain the
    /// tokens <c>{Date}</c> and <c>{Year}</c>, resolved against the user's calendar
    /// date at instantiation time.
    /// </summary>
    public string TitlePattern { get; set; } = string.Empty;

    public OutputType Type { get; set; }

    /// <summary>Default body structure — headings/scaffold the user fills in.</summary>
    public string ContentScaffold { get; set; } = string.Empty;

    /// <summary>
    /// Rule for pre-selecting source notes at instantiation. Never a fixed set of note
    /// ids — see <see cref="OutputTemplateSourceSelectionMode"/>.
    /// </summary>
    public OutputTemplateSourceSelectionMode DefaultSourceSelection { get; set; } = OutputTemplateSourceSelectionMode.None;

    /// <summary>
    /// True for the small starter set seeded automatically for every user so the
    /// feature is useful on day one.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}
