namespace Brainy.Domain.Enums;

/// <summary>
/// How an <see cref="Entities.OutputTemplate"/> pre-selects source notes when
/// instantiated. Never stores specific note ids on the template itself — templates are
/// reused across many instantiations — so this is a rule evaluated at instantiation
/// time against whatever project is supplied then.
/// </summary>
public enum OutputTemplateSourceSelectionMode
{
    /// <summary>No source notes are pre-selected.</summary>
    None = 0,

    /// <summary>
    /// Pre-selects every active (non-archived) note linked to the project supplied at
    /// instantiation. Has no effect if no project is supplied.
    /// </summary>
    ActiveProjectNotes = 1
}
