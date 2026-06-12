namespace Brainy.Web.Components.Help;

/// <summary>
/// Describes the in-page onboarding guide shown by the help wizard. Each guide
/// answers three questions for a screen: what it is for, how it maps to the
/// Second Brain (Tiago Forte) methodology, and how to use the page.
/// </summary>
public sealed record PageHelpGuide
{
    /// <summary>Stable identifier used to remember whether the user has seen this guide.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable name of the screen (e.g. "Inbox").</summary>
    public required string PageTitle { get; init; }

    /// <summary>Short supporting line shown under the title.</summary>
    public string? Tagline { get; init; }

    /// <summary>Ordered list of wizard steps.</summary>
    public required IReadOnlyList<PageHelpSection> Sections { get; init; }
}

/// <summary>A single step in the page help wizard.</summary>
public sealed record PageHelpSection
{
    /// <summary>Step heading.</summary>
    public required string Title { get; init; }

    /// <summary>MudBlazor icon string shown next to the heading.</summary>
    public required string Icon { get; init; }

    /// <summary>Introductory paragraph for the step.</summary>
    public string? Lead { get; init; }

    /// <summary>Supporting points. Rendered as bullets, or as numbered steps when <see cref="Ordered"/> is true.</summary>
    public IReadOnlyList<string> Points { get; init; } = [];

    /// <summary>When true, <see cref="Points"/> are rendered as an ordered (numbered) list.</summary>
    public bool Ordered { get; init; }
}
