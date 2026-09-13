namespace Brainy.Application.Services.ExternalImport;

/// <summary>
/// Decides whether attachment bytes recovered from an external source can become a
/// <see cref="Brainy.Domain.Entities.NoteImage"/>. Mirrors <c>NoteImageService</c>'s own
/// allow-list and size cap (10 MB) rather than inventing a separate policy, and — like
/// that service — excludes SVG (script-capable) even though it is an image format.
/// </summary>
internal static class AttachmentImportSupport
{
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf"
    };

    private static readonly HashSet<string> AllowedContentTypes =
        new(ContentTypesByExtension.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the content type for an importable attachment file name, or null when the
    /// file type is not one this import supports attaching (reported by the caller, never
    /// silently dropped).
    /// </summary>
    public static string? ResolveContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length > 0 && ContentTypesByExtension.TryGetValue(extension, out var contentType)
            ? contentType
            : null;
    }

    /// <summary>
    /// True when <paramref name="contentType"/> (e.g. a source's own declared MIME type,
    /// used when a file name/extension is not available) is one this import supports.
    /// </summary>
    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && AllowedContentTypes.Contains(contentType);

    /// <summary>Picks a file extension for a supported content type that arrived without a file name.</summary>
    public static string ExtensionForContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "application/pdf" => ".pdf",
        _ => ".bin"
    };
}
