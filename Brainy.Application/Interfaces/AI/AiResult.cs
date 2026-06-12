namespace Brainy.Application.Interfaces.AI;

/// <summary>Result of an AI operation with provenance metadata.</summary>
public record AiResult(
    string Content,
    string? Model,
    string PromptVersion,
    bool Success,
    string? ErrorMessage = null)
{
    /// <summary>Creates a failed result with the given error message.</summary>
    public static AiResult Failure(string error) =>
        new(string.Empty, null, "1.0", false, error);
}
