namespace Brainy.Application.DTOs.DataImport;

/// <summary>Summarizes the committed outcome of importing a Brainy data export.</summary>
public sealed record DataImportResultDto(
    string SchemaVersion,
    IReadOnlyList<DataImportEntityOutcomeDto> EntityOutcomes,
    IReadOnlyList<DataImportEntityCountDto> UnsupportedEntities,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> IntegrityIssues);
