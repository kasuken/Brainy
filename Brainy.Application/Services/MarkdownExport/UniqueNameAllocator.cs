namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Resolves name collisions within a scope (a folder, or the whole vault) by appending a
/// deterministic " (2)", " (3)", ... suffix — never by silently overwriting an existing
/// entry. Comparisons are case-insensitive because the export must stay safe on
/// case-insensitive filesystems (Windows, default macOS) even though the zip format
/// itself is case-sensitive.
/// </summary>
/// <remarks>
/// Callers must allocate names in a stable, deterministic order (e.g. sorted by creation
/// time then id) for two runs over the same data to produce the same file names.
/// </remarks>
public sealed class UniqueNameAllocator
{
    private readonly Dictionary<string, HashSet<string>> _takenByScope =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Allocates a unique stem for <paramref name="desiredStem"/> within <paramref name="scope"/>.
    /// Returns <paramref name="desiredStem"/> unchanged the first time it is requested in that
    /// scope; subsequent requests for the same stem (case-insensitively) get " (2)", " (3)", etc.,
    /// truncated as needed to respect <see cref="MarkdownFileNameSanitizer.MaxStemLength"/>.
    /// </summary>
    public string Allocate(string scope, string desiredStem)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(desiredStem);

        var taken = _takenByScope.TryGetValue(scope, out var existing)
            ? existing
            : _takenByScope[scope] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (taken.Add(desiredStem))
            return desiredStem;

        for (var suffixNumber = 2; ; suffixNumber++)
        {
            var suffix = $" ({suffixNumber})";
            var maxBaseLength = Math.Max(1, MarkdownFileNameSanitizer.MaxStemLength - suffix.Length);
            var truncatedBase = MarkdownFileNameSanitizer.TruncateSafely(desiredStem, maxBaseLength);
            truncatedBase = MarkdownFileNameSanitizer.TrimTrailingDotsAndSpaces(truncatedBase);
            if (truncatedBase.Length == 0)
                truncatedBase = MarkdownFileNameSanitizer.FallbackStem;

            var candidate = truncatedBase + suffix;
            if (taken.Add(candidate))
                return candidate;
        }
    }
}
