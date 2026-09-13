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
}
