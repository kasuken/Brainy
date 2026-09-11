namespace Brainy.Application.DTOs.DataImport;

/// <summary>
/// A dry-run plan for a Brainy export: what the import would create, reuse, or skip,
/// computed without mutating the database. Mirrors <see cref="DataImportResultDto"/>
/// so the confirmation UI can show the same shape before and after committing.
/// </summary>
public sealed record DataImportPreviewDto(
    string SchemaVersion,
    IReadOnlyList<DataImportEntityOutcomeDto> EntityOutcomes,
    IReadOnlyList<DataImportEntityCountDto> UnsupportedEntities,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> IntegrityIssues);
