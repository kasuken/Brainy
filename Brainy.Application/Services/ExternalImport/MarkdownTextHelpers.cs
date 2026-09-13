using System.Text.RegularExpressions;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Small, deliberately narrow Markdown/YAML-front-matter readers shared by the Obsidian,
/// Notion and generic-Markdown parsers. Every method only reads structure that is
/// literally present in the text — nothing here infers or fabricates a link, tag or
/// attachment that the source did not spell out.
/// </summary>
internal static partial class MarkdownTextHelpers
{
    /// <summary>
    /// Splits a leading <c>---\n ... \n---</c> YAML front-matter block off <paramref name="text"/>,
    /// returning the block's raw lines (for <see cref="ExtractFrontMatterTags"/>) and the
    /// remaining body. When there is no front matter, the whole input is the body.
    /// </summary>
    public static (IReadOnlyList<string> FrontMatterLines, string Body) SplitFrontMatter(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal) && normalized != "---")
            return ([], normalized);

        var closingIndex = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
            return ([], normalized);

        var frontMatter = normalized[4..closingIndex];
        // The closing fence may be followed by more dashes, whitespace, then a newline.
        var afterFence = normalized[(closingIndex + 4)..];
        var newlineIndex = afterFence.IndexOf('\n');
        var body = newlineIndex >= 0 ? afterFence[(newlineIndex + 1)..] : string.Empty;

        var lines = frontMatter.Split('\n');
        return (lines, body);
    }

    /// <summary>
    /// Reads a <c>tags:</c> entry out of front matter lines, supporting a flow list
    /// (<c>tags: [a, "b"]</c>), a block list (<c>tags:</c> followed by <c>- a</c> lines),
    /// or a bare comma-separated/single value on the same line.
    /// </summary>
    public static IReadOnlyList<string> ExtractFrontMatterTags(IReadOnlyList<string> frontMatterLines)
    {
        for (var i = 0; i < frontMatterLines.Count; i++)
        {
            var line = frontMatterLines[i];
            var match = TagsKeyRegex().Match(line);
            if (!match.Success)
                continue;

            var rest = match.Groups["value"].Value.Trim();
            if (rest.Length == 0)
            {
                // Block-list style: subsequent more-indented "- value" lines.
                var tags = new List<string>();
                for (var j = i + 1; j < frontMatterLines.Count; j++)
                {
                    var next = frontMatterLines[j];
                    var itemMatch = BlockListItemRegex().Match(next);
                    if (!itemMatch.Success)
                        break;

                    AddCleanedTag(tags, itemMatch.Groups["value"].Value);
                }

                return tags;
            }

            if (rest.StartsWith('[') && rest.EndsWith(']'))
                rest = rest[1..^1];

            return SplitTagList(rest);
        }

        return [];
    }

    /// <summary>Reads a scalar <c>key: value</c> front-matter entry, unquoting the value.</summary>
    public static string? ExtractFrontMatterScalar(IReadOnlyList<string> frontMatterLines, string key)
    {
        var prefix = key + ":";
        foreach (var line in frontMatterLines)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[prefix.Length..].Trim();
            return value.Length == 0 ? null : Unquote(value);
        }

        return null;
    }

    private static List<string> SplitTagList(string rest)
    {
        var tags = new List<string>();
        foreach (var part in rest.Split(','))
            AddCleanedTag(tags, part);

        return tags;
    }

    private static void AddCleanedTag(List<string> tags, string raw)
    {
        var cleaned = Unquote(raw.Trim()).Trim('#').Trim();
        if (cleaned.Length > 0)
            tags.Add(cleaned);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>Removes fenced (```) and inline (`) code so hashtag/link scans never match inside code.</summary>
    public static string StripCode(string body)
    {
        var withoutFences = FencedCodeRegex().Replace(body, match => new string(' ', match.Length));
        return InlineCodeRegex().Replace(withoutFences, match => new string(' ', match.Length));
    }

    /// <summary>Finds inline <c>#hashtags</c> (not headings, which need a space after the hash).</summary>
    public static IReadOnlyList<string> ExtractHashtags(string bodyWithoutCode)
    {
        var seen = new List<string>();
        foreach (Match match in HashtagRegex().Matches(bodyWithoutCode))
        {
            var tag = match.Groups["tag"].Value;
            if (!seen.Contains(tag, StringComparer.OrdinalIgnoreCase))
                seen.Add(tag);
        }

        return seen;
    }

    /// <summary>Finds Obsidian embeds: <c>![[target]]</c> or <c>![[target|alt]]</c>, used for attachments.</summary>
    public static IReadOnlyList<(string RawToken, string Target)> ExtractWikiEmbeds(string body)
    {
        var results = new List<(string, string)>();
        foreach (Match match in WikiEmbedRegex().Matches(body))
            results.Add((match.Value, CleanWikiTarget(match.Groups["target"].Value)));

        return results;
    }

    /// <summary>Finds Obsidian note links: <c>[[target]]</c> or <c>[[target|alt]]</c> (not preceded by '!').</summary>
    public static IReadOnlyList<(string RawToken, string Target)> ExtractWikiLinks(string body)
    {
        var results = new List<(string, string)>();
        foreach (Match match in WikiLinkRegex().Matches(body))
        {
            // Skip when this is actually an embed (preceded by '!').
            if (match.Index > 0 && body[match.Index - 1] == '!')
                continue;

            results.Add((match.Value, CleanWikiTarget(match.Groups["target"].Value)));
        }

        return results;
    }

    private static string CleanWikiTarget(string rawTarget)
    {
        var target = rawTarget;
        var pipeIndex = target.IndexOf('|');
        if (pipeIndex >= 0) target = target[..pipeIndex];

        var hashIndex = target.IndexOfAny(['#', '^']);
        if (hashIndex >= 0) target = target[..hashIndex];

        return target.Trim();
    }

    /// <summary>Finds Markdown image references: <c>![alt](path)</c>.</summary>
    public static IReadOnlyList<(string RawToken, string Path)> ExtractMarkdownImageRefs(string body)
    {
        var results = new List<(string, string)>();
        foreach (Match match in MarkdownImageRegex().Matches(body))
            results.Add((match.Value, CleanLinkPath(match.Groups["path"].Value)));

        return results;
    }

    /// <summary>Finds Markdown links: <c>[text](path)</c>, excluding image references.</summary>
    public static IReadOnlyList<(string RawToken, string Path)> ExtractMarkdownLinks(string body)
    {
        var results = new List<(string, string)>();
        foreach (Match match in MarkdownLinkRegex().Matches(body))
        {
            if (match.Index > 0 && body[match.Index - 1] == '!')
                continue;

            results.Add((match.Value, CleanLinkPath(match.Groups["path"].Value)));
        }

        return results;
    }

    private static string CleanLinkPath(string rawPath)
    {
        var path = rawPath.Trim();
        var titleIndex = path.IndexOf(' ');
        if (titleIndex >= 0) path = path[..titleIndex];

        try
        {
            return Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return path;
        }
    }

    /// <summary>
    /// Removes every image/embed/link token this shared parser recognises, so two renders
    /// of the same note (e.g. before and after attachment ids are minted) collapse to the
    /// same dedup key. Whitespace left behind by removed tokens is also collapsed.
    /// </summary>
    public static string NormalizeForDedup(string content)
    {
        var stripped = WikiEmbedRegex().Replace(content, string.Empty);
        stripped = MarkdownImageRegex().Replace(stripped, string.Empty);
        return WhitespaceRunRegex().Replace(stripped, " ").Trim();
    }

    [GeneratedRegex(@"^\s*tags\s*:\s*(?<value>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TagsKeyRegex();

    [GeneratedRegex(@"^\s{2,}-\s*(?<value>.+)$")]
    private static partial Regex BlockListItemRegex();

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"`[^`\n]*`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(?<![\w#/])#(?<tag>[\p{L}0-9_][\p{L}0-9_/-]*)")]
    private static partial Regex HashtagRegex();

    [GeneratedRegex(@"!\[\[(?<target>[^\]]+)\]\]")]
    private static partial Regex WikiEmbedRegex();

    [GeneratedRegex(@"\[\[(?<target>[^\]]+)\]\]")]
    private static partial Regex WikiLinkRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\((?<path>[^)]+)\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"\[[^\]]*\]\((?<path>[^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"[ \t]*\n[ \t\n]*|[ \t]{2,}")]
    private static partial Regex WhitespaceRunRegex();
}
