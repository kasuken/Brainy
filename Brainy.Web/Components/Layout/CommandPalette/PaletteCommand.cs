namespace Brainy.Web.Components.Layout.CommandPalette;

/// <summary>
/// What kind of thing a <see cref="PaletteCommand"/> does, used only to label and
/// group it distinctly from record search results in the command palette overlay.
/// </summary>
public enum PaletteCommandKind
{
    /// <summary>Jumps to a top-level surface (e.g. "Go to Projects").</summary>
    Navigation,

    /// <summary>Starts creating a new record (e.g. "New Task").</summary>
    Create,

    /// <summary>A contextual action tied to the user's current work (e.g. "Set current focus").</summary>
    Action,
}

/// <summary>
/// A single entry in the command palette: a named action the user can run without a
/// mouse. Every command resolves to a URL — navigation commands go straight to a
/// top-level surface, while creation and contextual-action commands add a query
/// flag that the destination page reads on load to open the same dialog its own
/// "New …" button would (see e.g. <c>Brainy.Web/Components/Pages/Home.razor</c>'s
/// <c>action=set-focus</c> handling). This keeps every entitlement check and piece
/// of creation logic exactly where it already lives — the palette never duplicates
/// it, so a Starter user at their project limit still gets the normal upgrade path.
/// </summary>
public sealed record PaletteCommand
{
    /// <summary>Stable id, used for usage tracking (recent/frequent ranking) and DOM ids.</summary>
    public required string Id { get; init; }

    /// <summary>Label shown in the palette, e.g. "Go to Projects".</summary>
    public required string Title { get; init; }

    /// <summary>Used only to render a distinguishing badge/icon; not for filtering.</summary>
    public required PaletteCommandKind Kind { get; init; }

    /// <summary>Relative URL the palette navigates to when this command runs.</summary>
    public required string Url { get; init; }

    /// <summary>MudBlazor icon string shown next to the command.</summary>
    public required string Icon { get; init; }

    /// <summary>Extra search terms the command matches on, beyond its title.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}
