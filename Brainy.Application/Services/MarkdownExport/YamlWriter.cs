using System.Text;

namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Minimal helper for writing valid YAML scalars into Markdown front matter. Deliberately
/// narrow: it only needs to cover the shapes the Markdown export produces (quoted strings,
/// booleans, UTC timestamps and flow string arrays), not general-purpose YAML emission.
/// </summary>
internal static class YamlWriter
{
    /// <summary>
    /// Renders <paramref name="value"/> as a double-quoted YAML scalar, escaping backslashes,
    /// double quotes and control characters so arbitrary user content (titles, note bodies,
    /// URLs) can never break out of the front matter block.
    /// </summary>
    public static string QuotedString(string? value)
    {
        if (value is null)
            return "null";

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                        continue;
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Renders a UTC instant as an ISO-8601 string YAML scalar (e.g. "2026-01-02T03:04:05Z").</summary>
    public static string UtcTimestamp(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return QuotedString(utc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    /// <summary>Renders a flow-style YAML sequence of quoted strings, e.g. <c>["a", "b"]</c>.</summary>
    public static string StringArray(IEnumerable<string> values)
        => "[" + string.Join(", ", values.Select(QuotedString)) + "]";
}
