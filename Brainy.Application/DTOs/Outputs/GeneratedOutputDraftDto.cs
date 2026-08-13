namespace Brainy.Application.DTOs.Outputs;

/// <summary>
/// An AI-generated output draft together with the provenance required to save it honestly.
/// </summary>
public sealed record GeneratedOutputDraftDto(
    string Content,
    string? Model,
    string? PromptVersion,
    IReadOnlyList<Guid> SourceNoteIds);
