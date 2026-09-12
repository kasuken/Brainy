namespace Brainy.Web.Components.Layout.CommandPalette;

/// <summary>
/// Pure filtering/ranking over <see cref="PaletteCommand"/>: matches free text against
/// a command's title and keywords, then orders recently-run commands first, then
/// most-frequently-run, then alphabetically. Kept free of Blazor/JS-interop so it can
/// be unit tested directly.
/// </summary>
public static class PaletteCommandRanker
{
    /// <summary>
    /// Returns the commands to show for the given query, ranked and capped at
    /// <paramref name="limit"/>. An empty/whitespace query matches every command
    /// (used for the default suggestion list shown when the palette opens empty).
    /// </summary>
    public static IReadOnlyList<PaletteCommand> Rank(
        IEnumerable<PaletteCommand> commands,
        string? query,
        IReadOnlyDictionary<string, int>? usageCounts,
        IReadOnlyList<string>? recentCommandIds,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var recentRank = BuildRecentRank(recentCommandIds);

        IEnumerable<PaletteCommand> candidates = string.IsNullOrWhiteSpace(query)
            ? commands
            : commands.Where(c => Matches(c, query.Trim()));

        return candidates
            .OrderBy(c => recentRank.TryGetValue(c.Id, out var idx) ? idx : int.MaxValue)
            .ThenByDescending(c => usageCounts is not null && usageCounts.TryGetValue(c.Id, out var count) ? count : 0)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToList();
    }

    private static Dictionary<string, int> BuildRecentRank(IReadOnlyList<string>? recentCommandIds)
    {
        var rank = new Dictionary<string, int>();
        if (recentCommandIds is null) return rank;

        // First occurrence wins so a command only counted once keeps its most-recent slot.
        for (var i = 0; i < recentCommandIds.Count; i++)
        {
            rank.TryAdd(recentCommandIds[i], i);
        }

        return rank;
    }

    private static bool Matches(PaletteCommand command, string query)
    {
        if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var keyword in command.Keywords)
        {
            if (keyword.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
