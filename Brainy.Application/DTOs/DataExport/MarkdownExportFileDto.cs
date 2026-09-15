namespace Brainy.Application.DTOs.DataExport;

/// <summary>
/// A ready-to-download Markdown/Obsidian-compatible vault, packaged as a zip archive.
/// Complements <see cref="DataExportFileDto"/> (the JSON round-trip format); this format
/// is for reading and using the export elsewhere, not for re-importing into Brainy.
/// </summary>
public sealed record MarkdownExportFileDto(
    string FileName,
    string ContentType,
    byte[] Content);
