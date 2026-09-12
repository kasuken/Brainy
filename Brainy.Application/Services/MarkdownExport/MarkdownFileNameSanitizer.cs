using System.Text;

namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Turns arbitrary, user-authored titles into deterministic, filesystem-safe name stems
/// for the Markdown/Obsidian export. A "stem" has no extension and no directory
/// separators; callers add the extension and resolve collisions separately (see
/// <see cref="UniqueNameAllocator"/>).
/// </summary>
/// <remarks>
/// Handles the full set of cross-platform edge cases called out by the export spec:
/// the reserved characters <c>/ \ : * ? " &lt; &gt; |</c>, leading/trailing dots and
/// spaces (both disallowed as trailing characters on Windows), the reserved Windows
/// device names (<c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c>-<c>COM9</c>,
/// <c>LPT1</c>-<c>LPT9</c>), overlong names, and titles that sanitize down to nothing.
/// Never returns an empty string.
/// </remarks>
public static class MarkdownFileNameSanitizer
{
    /// <summary>
    /// Maximum length, in UTF-16 code units, of a sanitized stem before collision
    /// suffixes are applied. Comfortably under every filesystem's path-component limit
    /// while leaving room for a " (NN)" disambiguation suffix.
    /// </summary>
    public const int MaxStemLength = 100;

    /// <summary>Fallback stem used when a title sanitizes down to nothing at all.</summary>
    public const string FallbackStem = "untitled";

    private static readonly char[] ReservedCharacters = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Sanitizes <paramref name="rawName"/> into a safe, non-empty file/folder name stem.</summary>
    public static string Sanitize(string? rawName)
    {
        var source = rawName ?? string.Empty;

        var builder = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (Array.IndexOf(ReservedCharacters, ch) >= 0 || char.IsControl(ch))
            {
                // A run of removed characters collapses to a single separating space
                // rather than gluing the surrounding text together (e.g. "a/b" -> "a b",
                // not "ab") so titles stay readable after sanitizing.
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                continue;
            }

            builder.Append(ch);
        }

        var candidate = CollapseWhitespace(builder.ToString());
        candidate = TrimTrailingDotsAndSpaces(candidate);
        candidate = TruncateSafely(candidate, MaxStemLength);
        candidate = TrimTrailingDotsAndSpaces(candidate);

        if (candidate.Length == 0)
            candidate = FallbackStem;

        if (IsReservedDeviceName(candidate))
            candidate += "_";

        return candidate;
    }

    /// <summary>
    /// True when <paramref name="stem"/> is (case-insensitively) one of the Windows
    /// reserved device names, on its own — the check that matters for a file stem, since
    /// Windows treats <c>CON.md</c> as reserved just as much as bare <c>CON</c>.
    /// </summary>
    public static bool IsReservedDeviceName(string stem) => ReservedDeviceNames.Contains(stem);

    /// <summary>
    /// Trims a candidate to at most <paramref name="maxLength"/> UTF-16 code units without
    /// splitting a surrogate pair in half.
    /// </summary>
    internal static string TruncateSafely(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;

        return value[..length];
    }

    internal static string TrimTrailingDotsAndSpaces(string value)
    {
        var end = value.Length;
        while (end > 0 && (value[end - 1] == '.' || value[end - 1] == ' '))
            end--;

        var start = 0;
        while (start < end && (value[start] == '.' || value[start] == ' '))
            start++;

        return value[start..end];
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value)
        {
            var isSpace = ch == ' ';
            if (isSpace && lastWasSpace)
                continue;

            builder.Append(ch);
            lastWasSpace = isSpace;
        }

        return builder.ToString();
    }
}
