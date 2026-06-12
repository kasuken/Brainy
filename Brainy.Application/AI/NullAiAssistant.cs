using Brainy.Application.Interfaces.AI;

namespace Brainy.Application.AI;

/// <summary>
/// No-op assistant used when no AI provider is configured.
/// Returns a descriptive failure message for every operation so callers can surface a
/// user-friendly "AI not configured" message rather than throwing.
/// </summary>
internal sealed class NullAiAssistant : IAiAssistant
{
    private static readonly Task<AiResult> _notConfigured =
        Task.FromResult(AiResult.Failure(
            "No AI provider is configured. Set AiAssistant:Provider in appsettings.json."));

    /// <inheritdoc/>
    public Task<AiResult> SummarizeAsync(string content, CancellationToken cancellationToken = default)
        => _notConfigured;

    /// <inheritdoc/>
    public Task<AiResult> SuggestParaClassificationAsync(string title, string content, CancellationToken cancellationToken = default)
        => _notConfigured;

    /// <inheritdoc/>
    public Task<AiResult> ExtractActionItemsAsync(string content, CancellationToken cancellationToken = default)
        => _notConfigured;

    /// <inheritdoc/>
    public Task<AiResult> DetectDuplicatesAsync(
        string noteTitle,
        string noteContent,
        IReadOnlyList<(Guid Id, string Title, string? Summary)> candidates,
        CancellationToken cancellationToken = default)
        => _notConfigured;

    /// <inheritdoc/>
    public Task<AiResult> GenerateOutputAsync(
        string outputTitle,
        string outputType,
        IReadOnlyList<string> sourceNoteContents,
        CancellationToken cancellationToken = default)
        => _notConfigured;
}
