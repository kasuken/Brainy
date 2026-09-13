using System.Text;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Parses a zip of Markdown files into <see cref="ParsedImportNote"/>s. Used for both
/// <see cref="Brainy.Application.DTOs.DataImport.ExternalImportSourceFormat.ObsidianVault"/>
/// and <see cref="Brainy.Application.DTOs.DataImport.ExternalImportSourceFormat.MarkdownFolder"/>
/// — an Obsidian vault is, structurally, exactly this: Markdown files with optional YAML
/// front matter, <c>[[wiki links]]</c>, inline <c>#hashtags</c>, and an attachments
/// folder. Deliberately mirrors the shape <c>MarkdownExportService</c> produces (front
/// matter keys, wiki-link and image-path conventions) so a vault this app exported
/// round-trips back in.
/// </summary>
internal static class MarkdownVaultParser
{
    public static ExternalImportParseResult Parse(Stream archiveStream)
    {
        var archive = SafeArchiveReader.Read(archiveStream);
        var warnings = new List<string>(archive.RejectedEntries);

        var markdownPaths = archive.Entries.Keys
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            // A root-level README.md is the near-universal convention for vault/export
            // metadata (including Brainy's own MarkdownExportService), never a note.
            .Where(path => !string.Equals(path, "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Wiki links resolve to a note by its file's base name (ignoring folder and
        // extension) — the same convention Obsidian itself uses and that
        // MarkdownExportService relies on when it writes [[FileStem]] links.
        var keyByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in markdownPaths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!keyByStem.TryAdd(stem, path))
                ambiguousStems.Add(stem);
        }

        foreach (var stem in ambiguousStems)
        {
            warnings.Add(
                $"Multiple notes are named '{stem}'; [[wiki links]] to it resolve to only one of them.");
        }

        var notes = new List<ParsedImportNote>();
        foreach (var path in markdownPaths)
        {
            var text = DecodeText(archive.Entries[path]);
            var (frontMatterLines, body) = MarkdownTextHelpers.SplitFrontMatter(text);

            var title = MarkdownTextHelpers.ExtractFrontMatterScalar(frontMatterLines, "title")
                ?? DeriveTitleFromBody(body)
                ?? Path.GetFileNameWithoutExtension(path);

            var codeless = MarkdownTextHelpers.StripCode(body);
            var tags = MarkdownTextHelpers.ExtractFrontMatterTags(frontMatterLines)
                .Concat(MarkdownTextHelpers.ExtractHashtags(codeless))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var linkTargetKeys = MarkdownTextHelpers.ExtractWikiLinks(body)
                .Select(link => keyByStem.GetValueOrDefault(link.Target))
                .Where(key => key is not null && !string.Equals(key, path, StringComparison.OrdinalIgnoreCase))
                .Select(key => key!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var candidates = MarkdownTextHelpers.ExtractWikiEmbeds(body)
                .Concat(MarkdownTextHelpers.ExtractMarkdownImageRefs(body));
            var attachments = AttachmentResolver.Resolve(path, candidates, archive.Entries, title, warnings);

            notes.Add(new ParsedImportNote(path, title, body, tags, linkTargetKeys, attachments));
        }

        return new ExternalImportParseResult(notes, warnings);
    }

    /// <summary>Falls back to the first Markdown heading (<c># Heading</c>) as a title.</summary>
    private static string? DeriveTitleFromBody(string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();

            return null;
        }

        return null;
    }

    private static string DecodeText(byte[] bytes)
    {
        // Strip a UTF-8 BOM if present; Encoding.UTF8.GetString does not do this itself.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }
}
