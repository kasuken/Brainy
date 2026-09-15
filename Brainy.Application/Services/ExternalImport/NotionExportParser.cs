using System.Text;
using System.Text.RegularExpressions;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Parses a Notion "Markdown &amp; CSV" export zip into <see cref="ParsedImportNote"/>s.
/// One page ⇒ one note; one database (CSV) row ⇒ one note, since a spreadsheet-shaped
/// database does not carry enough structure to safely guess whether it should become a
/// Project or a Resource — guessing that would fabricate structure Notion itself never
/// stated, so every database row lands in the Inbox exactly like every page does.
///
/// Notion appends a 32-character hex id to every exported file/folder name to keep them
/// unique (<c>My Page 3f2504e04f8911d39a0c0305e82c3301.md</c>); that id is stripped back
/// off for the note's title. Notion writes a short "Key: value" property block at the top
/// of some page exports — the sole field this parser reads out of it is a literal
/// <c>Tags:</c>/<c>Tag:</c> line (removed from the body once read); anything else in that
/// block is left as ordinary note content, never interpreted.
/// </summary>
internal static partial class NotionExportParser
{
    public static ExternalImportParseResult Parse(Stream archiveStream)
    {
        var archive = SafeArchiveReader.Read(archiveStream);
        var warnings = new List<string>(archive.RejectedEntries);
        var notes = new List<ParsedImportNote>();

        var markdownPaths = archive.Entries.Keys
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        foreach (var path in markdownPaths)
        {
            var text = DecodeText(archive.Entries[path]);
            var (frontMatterLines, afterFrontMatter) = MarkdownTextHelpers.SplitFrontMatter(text);
            var title = DeriveTitle(path);
            var (propertyTags, body) = ExtractAndStripTagsProperty(afterFrontMatter);

            var codeless = MarkdownTextHelpers.StripCode(body);
            var tags = MarkdownTextHelpers.ExtractFrontMatterTags(frontMatterLines)
                .Concat(propertyTags)
                .Concat(MarkdownTextHelpers.ExtractHashtags(codeless))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var linkTargetKeys = new List<string>();
            foreach (var (_, linkPath) in MarkdownTextHelpers.ExtractMarkdownLinks(body))
            {
                if (!linkPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;

                var resolved = VaultPathResolver.Combine(path, linkPath);
                if (archive.Entries.ContainsKey(resolved) && !string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase))
                    linkTargetKeys.Add(resolved);
            }

            var candidates = MarkdownTextHelpers.ExtractMarkdownImageRefs(body);
            var attachments = AttachmentResolver.Resolve(path, candidates, archive.Entries, title, warnings);

            notes.Add(new ParsedImportNote(
                path, title, body, tags, linkTargetKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), attachments));
        }

        var csvPaths = archive.Entries.Keys
            .Where(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        foreach (var path in csvPaths)
        {
            var rows = ParseCsv(DecodeText(archive.Entries[path]));
            if (rows.Count == 0)
            {
                warnings.Add($"Database '{path}' has no rows and was skipped.");
                continue;
            }

            var header = rows[0];
            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                var (title, content, tags) = BuildRowNote(header, rows[rowIndex]);
                notes.Add(new ParsedImportNote($"{path}#row{rowIndex}", title, content, tags, [], []));
            }
        }

        return new ExternalImportParseResult(notes, warnings);
    }

    /// <summary>
    /// Notion suffixes every exported page/database file and folder name with a 32-hex-
    /// character id to guarantee uniqueness; the title is everything before that.
    /// </summary>
    private static string DeriveTitle(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var match = NotionIdSuffixRegex().Match(stem);
        var title = match.Success ? match.Groups["name"].Value.Trim() : stem.Trim();
        return title.Length > 0 ? title : stem;
    }

    /// <summary>
    /// Reads a literal <c>Tags:</c>/<c>Tag:</c> line out of the first few lines of a page
    /// body (Notion's own lightweight property block) and removes just that line —
    /// everything else in the body, including any other property-looking line, is left
    /// untouched as ordinary content.
    /// </summary>
    private static (IReadOnlyList<string> Tags, string Body) ExtractAndStripTagsProperty(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var tags = new List<string>();
        var kept = new List<string>(lines.Length);
        var inHeaderZone = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (inHeaderZone && i < 15)
            {
                var trimmed = line.Trim();
                var match = TagsPropertyLineRegex().Match(trimmed);
                if (match.Success)
                {
                    foreach (var part in match.Groups["value"].Value.Split(','))
                    {
                        var tag = part.Trim();
                        if (tag.Length > 0) tags.Add(tag);
                    }

                    continue; // Drop this line from the body.
                }

                if (trimmed.Length > 0 && !SimplePropertyLineRegex().IsMatch(trimmed))
                    inHeaderZone = false;
            }

            kept.Add(line);
        }

        return (tags, string.Join('\n', kept));
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < normalized.Length && normalized[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static (string Title, string Content, IReadOnlyList<string> Tags) BuildRowNote(
        List<string> header, List<string> row)
    {
        string? title = null;
        var tags = new List<string>();
        var lines = new List<string>();

        for (var i = 0; i < header.Count; i++)
        {
            var column = header[i].Trim();
            var value = i < row.Count ? row[i].Trim() : string.Empty;
            if (value.Length == 0) continue;

            if (title is null && (string.Equals(column, "Name", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(column, "Title", StringComparison.OrdinalIgnoreCase)))
            {
                title = value;
                continue;
            }

            if (string.Equals(column, "Tags", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(column, "Tag", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var part in value.Split(','))
                {
                    var tag = part.Trim();
                    if (tag.Length > 0) tags.Add(tag);
                }

                continue;
            }

            lines.Add($"**{column}:** {value}");
        }

        title ??= row.Count > 0 ? row[0].Trim() : string.Empty;
        if (title.Length == 0) title = "Untitled";

        return (title, string.Join('\n', lines), tags);
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    [GeneratedRegex(@"^(?<name>.*?)[ _]?(?<id>[0-9a-fA-F]{32})$")]
    private static partial Regex NotionIdSuffixRegex();

    [GeneratedRegex(@"^tags?\s*:\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TagsPropertyLineRegex();

    [GeneratedRegex(@"^[\p{L}][\p{L}0-9 _-]{0,40}:\s*.*$")]
    private static partial Regex SimplePropertyLineRegex();
}
