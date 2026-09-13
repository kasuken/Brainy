namespace Brainy.Application.DTOs.DataImport;

/// <summary>
/// Summarizes the committed outcome of importing a non-Brainy source (Obsidian, Notion,
/// a generic Markdown folder, or Evernote).
/// </summary>
public sealed record ExternalImportResultDto(
    string SourceFormat,
    IReadOnlyList<DataImportEntityOutcomeDto> EntityOutcomes,
    IReadOnlyList<DataImportEntityCountDto> UnmappedAttachments,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> IntegrityIssues);
