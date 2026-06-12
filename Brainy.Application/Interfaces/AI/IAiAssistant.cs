namespace Brainy.Application.Interfaces.AI;

/// <summary>Provider-agnostic AI assistant for Brainy operations.</summary>
public interface IAiAssistant
{
    /// <summary>Generates a plain-text summary of the given content.</summary>
    Task<AiResult> SummarizeAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>Suggests a PARA category and up to 5 tags based on title and content.</summary>
    Task<AiResult> SuggestParaClassificationAsync(string title, string content, CancellationToken cancellationToken = default);

    /// <summary>Extracts action items from the given content.</summary>
    Task<AiResult> ExtractActionItemsAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects potentially duplicate notes given a note title and content and a collection of candidate note summaries.
    /// </summary>
    Task<AiResult> DetectDuplicatesAsync(
        string noteTitle,
        string noteContent,
        IReadOnlyList<(Guid Id, string Title, string? Summary)> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Generates a draft output from source notes content.</summary>
    Task<AiResult> GenerateOutputAsync(
        string outputTitle,
        string outputType,
        IReadOnlyList<string> sourceNoteContents,
        CancellationToken cancellationToken = default);
}
