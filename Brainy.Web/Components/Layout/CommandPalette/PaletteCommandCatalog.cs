using MudBlazor;

namespace Brainy.Web.Components.Layout.CommandPalette;

/// <summary>
/// The fixed list of commands the palette can run: navigation to every top-level
/// surface, creation shortcuts, and contextual actions (issue #318). Kept as plain
/// data — no service or database dependency — so it can be listed and matched in a
/// unit test without spinning up EF Core or a DbContext.
/// </summary>
public static class PaletteCommandCatalog
{
    /// <summary>Every command the palette can offer, in catalog (default) order.</summary>
    public static IReadOnlyList<PaletteCommand> All { get; } = BuildAll();

    private static IReadOnlyList<PaletteCommand> BuildAll() =>
    [
        // ── Navigation: every top-level surface ────────────────────────────
        Nav("nav-today", "Go to Today", "/today", Icons.Material.Outlined.WbSunny, "home", "dashboard", "current focus"),
        Nav("nav-inbox", "Go to Inbox", "/inbox", Icons.Material.Outlined.Inbox, "capture", "unprocessed"),
        Nav("nav-projects", "Go to Projects", "/projects", Icons.Material.Outlined.FolderSpecial),
        Nav("nav-tasks-hub", "Go to Tasks Hub", "/tasks-hub", Icons.Material.Outlined.TaskAlt, "tasks"),
        Nav("nav-tasks-calendar", "Go to Tasks Calendar", "/tasks-calendar", Icons.Material.Outlined.CalendarMonth, "tasks", "calendar", "schedule"),
        Nav("nav-notes", "Go to Notes", "/notes", Icons.Material.Outlined.Notes),
        Nav("nav-search", "Go to full search", "/search", Icons.Material.Outlined.Search, "find"),
        Nav("nav-para", "Go to PARA overview", "/para", Icons.Material.Outlined.Dashboard, "overview"),
        Nav("nav-areas", "Go to Areas", "/areas", Icons.Material.Outlined.Category),
        Nav("nav-resources", "Go to Resources", "/resources", Icons.Material.Outlined.LibraryBooks),
        Nav("nav-goals", "Go to Goals", "/goals", Icons.Material.Outlined.TrackChanges),
        Nav("nav-ideas", "Go to Ideas", "/ideas", Icons.Material.Outlined.EmojiObjects),
        Nav("nav-tags", "Go to Tags", "/tags", Icons.Material.Outlined.Sell, "labels"),
        Nav("nav-archives", "Go to Archives", "/archives", Icons.Material.Outlined.Archive),
        Nav("nav-pulse", "Go to Pulse", "/pulse", Icons.Material.Outlined.Insights, "analytics", "streaks"),
        Nav("nav-outputs", "Go to Outputs", "/outputs", Icons.Material.Outlined.Output, "deliverables"),
        Nav("nav-llm", "Go to LLM context export", "/llm", Icons.Material.Outlined.Psychology, "llm", "context", "export", "prompt"),

        // ── Creation ─────────────────────────────────────────────────────
        Create("create-note", "New note", "/notes?new=true", Icons.Material.Outlined.NoteAdd),
        Create("create-task", "New task", "/today?action=new-task", Icons.Material.Outlined.AddTask),
        Create("create-project", "New project", "/projects?new=true", Icons.Material.Outlined.CreateNewFolder),
        Create("create-idea", "New idea", "/ideas/new", Icons.Material.Outlined.EmojiObjects),
        Create("create-output", "New output", "/outputs?new=true", Icons.Material.Outlined.NoteAdd, "deliverable"),

        // ── Contextual actions ───────────────────────────────────────────
        Action("action-set-focus", "Set current focus", "/today?action=set-focus", Icons.Material.Outlined.CenterFocusStrong, "task"),
        Action("action-weekly-review", "Start weekly review", "/today/week/review", Icons.Material.Outlined.FactCheck, "week", "plan"),
        Action("action-process-inbox", "Process Inbox", "/inbox", Icons.Material.Outlined.AllInbox, "capture", "triage"),
    ];

    private static PaletteCommand Nav(string id, string title, string url, string icon, params string[] keywords) =>
        new()
        {
            Id = id,
            Title = title,
            Kind = PaletteCommandKind.Navigation,
            Url = url,
            Icon = icon,
            Keywords = keywords,
        };

    private static PaletteCommand Create(string id, string title, string url, string icon, params string[] keywords) =>
        new()
        {
            Id = id,
            Title = title,
            Kind = PaletteCommandKind.Create,
            Url = url,
            Icon = icon,
            Keywords = [.. keywords, "create", "new"],
        };

    private static PaletteCommand Action(string id, string title, string url, string icon, params string[] keywords) =>
        new()
        {
            Id = id,
            Title = title,
            Kind = PaletteCommandKind.Action,
            Url = url,
            Icon = icon,
            Keywords = keywords,
        };
}
