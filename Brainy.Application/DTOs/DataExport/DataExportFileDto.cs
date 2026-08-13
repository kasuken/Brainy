namespace Brainy.Application.DTOs.DataExport;

/// <summary>A ready-to-download, versioned Brainy data archive.</summary>
public sealed record DataExportFileDto(
    string FileName,
    string ContentType,
    string SchemaVersion,
    byte[] Content);
