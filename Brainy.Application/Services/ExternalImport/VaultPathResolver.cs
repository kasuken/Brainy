namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Resolves a relative link/attachment path found inside one archive entry against the
/// in-memory entry map built by <see cref="SafeArchiveReader"/>. Shared by the
/// Markdown/Obsidian and Notion parsers, which both link files to each other with
/// ordinary relative paths.
/// </summary>
internal static class VaultPathResolver
{
    /// <summary>
    /// Combines <paramref name="target"/> against the directory of <paramref name="fromPath"/>
    /// (or the archive root when <paramref name="fromPath"/> is empty), resolving <c>.</c>
    /// and <c>..</c> segments. Never throws and never escapes the archive's own in-memory
    /// key space — this only ever produces a candidate dictionary key, it does not touch
    /// the file system.
    /// </summary>
    public static string Combine(string fromPath, string target)
    {
        var baseDir = fromPath.Length == 0
            ? string.Empty
            : Path.GetDirectoryName(fromPath.Replace('\\', '/')) ?? string.Empty;

        var combined = target.StartsWith('/')
            ? target.TrimStart('/')
            : baseDir.Length > 0 ? baseDir + "/" + target : target;

        var parts = new List<string>();
        foreach (var segment in combined.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }
}
