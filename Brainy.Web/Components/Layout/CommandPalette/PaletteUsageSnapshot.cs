namespace Brainy.Web.Components.Layout.CommandPalette;

/// <summary>
/// Per-browser command usage, persisted client-side (localStorage, via
/// <c>brainySearch.js</c>) rather than in the database — issue #318 explicitly
/// requires no schema change, and ranking "recent and frequent" commands is a
/// per-device UI convenience, not a business record.
/// </summary>
/// <param name="Counts">Total times each command id has been run.</param>
/// <param name="RecentIds">Command ids most-recently-run first, capped client-side.</param>
public sealed record PaletteUsageSnapshot(Dictionary<string, int>? Counts, List<string>? RecentIds);
