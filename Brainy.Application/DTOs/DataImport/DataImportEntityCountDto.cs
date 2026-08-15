namespace Brainy.Application.DTOs.DataImport;

/// <summary>Summarizes how many records were found for an entity type during import.</summary>
public sealed record DataImportEntityCountDto(
    string EntityType,
    int Count);
