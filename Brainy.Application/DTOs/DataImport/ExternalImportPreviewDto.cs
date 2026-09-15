namespace Brainy.Application.DTOs.DataImport;

/// <summary>
/// A dry-run plan for importing a non-Brainy source (Obsidian, Notion, a generic
/// Markdown folder, or Evernote): what the import would create, reuse, or skip,
/// computed without mutating the database. Mirrors <see cref="DataImportPreviewDto"/>
/// so the same confirmation UI pattern applies before and after committing.
/// </summary>
public sealed record ExternalImportPreviewDto(
    string SourceFormat,
    IReadOnlyList<DataImportEntityOutcomeDto> EntityOutcomes,
    IReadOnlyList<DataImportEntityCountDto> UnmappedAttachments,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> IntegrityIssues);
