namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Locates the bytes for attachment/image references found inside a note's body and
/// turns each into a <see cref="ParsedAttachment"/> — or, when it cannot be safely or
/// faithfully imported (missing, oversized, unsupported type), records why in
/// <paramref name="warnings"/> and leaves the reference untouched in the note's content.
/// Shared by the Markdown/Obsidian and Notion parsers.
/// </summary>
internal static class AttachmentResolver
{
    public static List<ParsedAttachment> Resolve(
        string notePath,
        IEnumerable<(string RawToken, string Target)> candidates,
        IReadOnlyDictionary<string, byte[]> entries,
        string noteTitle,
        List<string> warnings)
    {
        var attachments = new List<ParsedAttachment>();

        foreach (var (rawToken, target) in candidates)
        {
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(target);
            if (fileName.Length == 0)
                continue;

            if (!TryResolveEntry(notePath, target, entries, out var data))
            {
                warnings.Add($"Attachment '{fileName}' referenced by note '{noteTitle}' was not found; left unresolved in the note.");
                continue;
            }

            if (data.LongLength > AttachmentImportSupport.MaxAttachmentBytes)
            {
                warnings.Add(
                    $"Attachment '{fileName}' referenced by note '{noteTitle}' exceeds {AttachmentImportSupport.MaxAttachmentBytes / (1024 * 1024)} MB and was not imported; left unresolved in the note.");
                continue;
            }

            var contentType = AttachmentImportSupport.ResolveContentType(fileName);
            if (contentType is null)
            {
                warnings.Add($"Attachment '{fileName}' referenced by note '{noteTitle}' is not a supported file type and was not imported; left unresolved in the note.");
                continue;
            }

            attachments.Add(new ParsedAttachment(rawToken, fileName, contentType, data));
        }

        return attachments;
    }

    /// <summary>
    /// Resolves an attachment reference relative to the referencing note's folder first
    /// (the common case), then as an archive-root path, then — matching how Obsidian
    /// itself resolves a bare file name — as the unique entry anywhere in the archive
    /// with that file name.
    /// </summary>
    private static bool TryResolveEntry(
        string notePath, string target, IReadOnlyDictionary<string, byte[]> entries, out byte[] data)
    {
        var relative = VaultPathResolver.Combine(notePath, target);
        if (entries.TryGetValue(relative, out data!))
            return true;

        var rooted = VaultPathResolver.Combine(string.Empty, target);
        if (entries.TryGetValue(rooted, out data!))
            return true;

        var fileName = Path.GetFileName(target);
        var matches = entries.Where(e => string.Equals(Path.GetFileName(e.Key), fileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            data = matches[0].Value;
            return true;
        }

        data = [];
        return false;
    }
}
