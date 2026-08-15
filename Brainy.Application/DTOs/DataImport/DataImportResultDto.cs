namespace Brainy.Application.DTOs.DataImport;

/// <summary>Summarizes the outcome of importing a Brainy data export.</summary>
public sealed record DataImportResultDto(
    string SchemaVersion,
    int ImportedTags,
    int ReusedTags,
    int SkippedTags,
    int ImportedNotes,
    int SkippedNotes,
    int LinkedNoteTags,
    int SkippedNoteTagLinks,
    IReadOnlyList<DataImportEntityCountDto> UnsupportedEntities);
