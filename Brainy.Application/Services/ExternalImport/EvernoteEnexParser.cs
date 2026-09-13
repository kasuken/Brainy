using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Parses an Evernote <c>.enex</c> export (a single XML document, one <c>&lt;note&gt;</c>
/// per Evernote note) into <see cref="ParsedImportNote"/>s.
///
/// Security: <c>.enex</c> is XML, so it is parsed with DTD processing prohibited and no
/// <see cref="XmlResolver"/> — the same defensive posture <see cref="SafeArchiveReader"/>
/// takes against zip-based attacks, applied here against XXE/entity-expansion attacks.
///
/// Each note's <c>&lt;content&gt;</c> is Evernote's own restricted HTML dialect (ENML); it
/// is mechanically converted to Markdown-ish plain text (bold/italic/links/lists), never
/// re-interpreted. Evernote's own <c>&lt;tag&gt;</c> elements map directly onto Brainy
/// tags. Each <c>&lt;resource&gt;</c> (an attachment's base64 bytes) is matched back to
/// its inline <c>&lt;en-media hash="..."/&gt;</c> reference by MD5 hash — the same hash
/// Evernote itself uses to link a resource into the note body.
/// </summary>
internal static partial class EvernoteEnexParser
{
    private const int MaxNoteCount = 20_000;

    public static ExternalImportParseResult Parse(Stream content)
    {
        var warnings = new List<string>();
        var notes = new List<ParsedImportNote>();

        var document = LoadSafely(content);
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "en-export", StringComparison.Ordinal))
            throw new InvalidOperationException("The uploaded file is not a valid Evernote (.enex) export.");

        var noteElements = root.Elements("note").ToList();
        if (noteElements.Count > MaxNoteCount)
        {
            throw new InvalidOperationException(
                $"The export contains {noteElements.Count} notes, more than the {MaxNoteCount} this import allows.");
        }

        var index = 0;
        foreach (var noteElement in noteElements)
        {
            index++;
            var title = ((string?)noteElement.Element("title"))?.Trim();
            if (string.IsNullOrEmpty(title)) title = $"Untitled note {index}";

            var tags = noteElement.Elements("tag")
                .Select(e => e.Value.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var resourcesByHash = LoadResources(noteElement, title, warnings);

            var html = noteElement.Element("content")?.Value ?? string.Empty;
            var body = ConvertEnNoteToText(html, out var referencedHashes);
            body = AppendOrphanAttachments(body, resourcesByHash, referencedHashes);

            var attachments = ResolveAttachments(body, resourcesByHash, title, warnings);
            notes.Add(new ParsedImportNote($"note-{index}", title, body, tags, [], attachments));
        }

        return new ExternalImportParseResult(notes, warnings);
    }

    private static XDocument LoadSafely(Stream content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 200_000_000
        };

        try
        {
            using var reader = XmlReader.Create(content, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException("The uploaded file is not a valid Evernote (.enex) export.", ex);
        }
    }

    private sealed record EvernoteResource(byte[] Data, string? Mime, string? FileName);

    private static Dictionary<string, EvernoteResource> LoadResources(
        XElement noteElement, string title, List<string> warnings)
    {
        var resourcesByHash = new Dictionary<string, EvernoteResource>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceElement in noteElement.Elements("resource"))
        {
            var base64 = resourceElement.Element("data")?.Value;
            if (string.IsNullOrWhiteSpace(base64))
                continue;

            byte[] data;
            try
            {
                data = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                warnings.Add($"A resource in note '{title}' had unreadable data and was skipped.");
                continue;
            }

            if (data.LongLength > AttachmentImportSupport.MaxAttachmentBytes)
            {
                warnings.Add(
                    $"A resource in note '{title}' exceeds {AttachmentImportSupport.MaxAttachmentBytes / (1024 * 1024)} MB and was not imported.");
                continue;
            }

            var mime = resourceElement.Element("mime")?.Value.Trim();
            var fileName = resourceElement.Element("resource-attributes")?.Element("file-name")?.Value.Trim();
            var hash = Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
            resourcesByHash[hash] = new EvernoteResource(data, mime, fileName);
        }

        return resourcesByHash;
    }

    /// <summary>
    /// Mechanically converts Evernote's ENML (a restricted HTML dialect) to plain
    /// Markdown-ish text: paragraphs/line breaks/lists become newlines, bold/italic/links
    /// become Markdown equivalents, and <c>&lt;en-media hash="H"/&gt;</c> references
    /// become <c>![resource](enmedia:H)</c> — an ordinary-looking Markdown image
    /// reference so the same attachment-resolution convention as the other parsers
    /// applies. Every hash actually referenced this way is added to
    /// <paramref name="referencedHashes"/> so orphaned resources can be appended
    /// separately, exactly as <c>MarkdownExportService</c> does for images a note's
    /// content never linked to.
    /// </summary>
    private static string ConvertEnNoteToText(string html, out HashSet<string> referencedHashes)
    {
        var text = EnMediaRegex().Replace(html, match => match.Groups["hash"].Value.Length > 0
            ? $"![resource](enmedia:{match.Groups["hash"].Value.ToLowerInvariant()})"
            : string.Empty);

        referencedHashes = EnMediaHashOutputRegex().Matches(text)
            .Select(m => m.Groups["hash"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        text = AnchorRegex().Replace(text, "[$2]($1)");
        text = BoldOpenRegex().Replace(text, "**");
        text = BoldCloseRegex().Replace(text, "**");
        text = ItalicOpenRegex().Replace(text, "_");
        text = ItalicCloseRegex().Replace(text, "_");
        text = ListItemOpenRegex().Replace(text, "- ");
        text = BreakingTagRegex().Replace(text, "\n");
        text = RemainingTagRegex().Replace(text, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = ExcessBlankLinesRegex().Replace(text, "\n\n");

        return text.Trim();
    }

    private static string AppendOrphanAttachments(
        string body, Dictionary<string, EvernoteResource> resourcesByHash, HashSet<string> referencedHashes)
    {
        var orphans = resourcesByHash.Keys.Where(hash => !referencedHashes.Contains(hash)).ToList();
        if (orphans.Count == 0)
            return body;

        var appended = string.Join('\n', orphans.Select(hash => $"![resource](enmedia:{hash})"));
        return body.Length == 0 ? appended : $"{body}\n\n{appended}";
    }

    private static List<ParsedAttachment> ResolveAttachments(
        string body, Dictionary<string, EvernoteResource> resourcesByHash, string title, List<string> warnings)
    {
        var attachments = new List<ParsedAttachment>();
        var index = 0;

        foreach (var (rawToken, path) in MarkdownTextHelpers.ExtractMarkdownImageRefs(body))
        {
            if (!path.StartsWith("enmedia:", StringComparison.Ordinal))
                continue;

            index++;
            var hash = path["enmedia:".Length..];
            if (!resourcesByHash.TryGetValue(hash, out var resource))
            {
                warnings.Add($"An attachment referenced by note '{title}' could not be matched to its data and was not imported.");
                continue;
            }

            var contentType = resource.FileName is { Length: > 0 }
                ? AttachmentImportSupport.ResolveContentType(resource.FileName)
                : null;
            contentType ??= AttachmentImportSupport.IsAllowedContentType(resource.Mime) ? resource.Mime : null;

            if (contentType is null)
            {
                warnings.Add($"An attachment in note '{title}' is not a supported file type and was not imported.");
                continue;
            }

            var fileName = resource.FileName is { Length: > 0 }
                ? resource.FileName
                : $"attachment-{index}{AttachmentImportSupport.ExtensionForContentType(contentType)}";

            attachments.Add(new ParsedAttachment(rawToken, fileName, contentType, resource.Data));
        }

        return attachments;
    }

    [GeneratedRegex("""<en-media[^>]*\bhash="(?<hash>[0-9a-fA-F]*)"[^>]*/?>""", RegexOptions.IgnoreCase)]
    private static partial Regex EnMediaRegex();

    [GeneratedRegex(@"enmedia:(?<hash>[0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EnMediaHashOutputRegex();

    [GeneratedRegex("""<a\s+[^>]*href="(?<href>[^"]*)"[^>]*>(?<text>.*?)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<(b|strong)(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex BoldOpenRegex();

    [GeneratedRegex(@"</(b|strong)>", RegexOptions.IgnoreCase)]
    private static partial Regex BoldCloseRegex();

    [GeneratedRegex(@"<(i|em)(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex ItalicOpenRegex();

    [GeneratedRegex(@"</(i|em)>", RegexOptions.IgnoreCase)]
    private static partial Regex ItalicCloseRegex();

    [GeneratedRegex(@"<li(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemOpenRegex();

    [GeneratedRegex(@"<(br|/div|/p|/li|/ul|/ol)(\s[^>]*)?/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakingTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex RemainingTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();
}
