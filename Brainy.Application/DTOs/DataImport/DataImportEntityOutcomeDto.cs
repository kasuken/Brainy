namespace Brainy.Application.DTOs.DataImport;

/// <summary>
/// Reports how many rows of one entity type an import created, reused (matched an
/// existing row for this user and left it untouched), or skipped (an invalid row or
/// one whose required relationships could not be resolved).
/// </summary>
public sealed record DataImportEntityOutcomeDto(
    string EntityType,
    int Created,
    int Reused,
    int Skipped);
