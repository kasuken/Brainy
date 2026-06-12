namespace Brainy.Application.AI.Prompts;

/// <summary>Versioned system and user prompt templates for all AI operations.</summary>
/// <remarks>
/// Version strings are stored in <see cref="AiResult.PromptVersion"/> so callers can detect
/// when a result was produced with an older prompt and re-run if needed.
/// </remarks>
public static class AiPrompts
{
    public const string SummarizeVersion = "summarize-v1";
    public const string ClassifyVersion = "classify-v1";
    public const string ExtractActionsVersion = "extract-actions-v1";
    public const string DuplicateDetectVersion = "duplicate-detect-v1";
    public const string GenerateOutputVersion = "generate-output-v1";

    public static string SummarizeSystem =>
        "You are a concise knowledge assistant. Summarize the provided note content into 2-4 sentences " +
        "that capture the key ideas. Be factual, preserve nuance, and do not invent details. " +
        "Respond with plain text only.";

    public static string ClassifySystem =>
        "You are a second-brain assistant using the PARA method. Given a note title and content, " +
        "respond in JSON with: { \"category\": \"Project|Area|Resource|Archive\", \"tags\": [\"tag1\",\"tag2\"] } " +
        "(max 5 tags, lowercase, no spaces). Respond ONLY with valid JSON.";

    public static string ExtractActionsSystem =>
        "You are a productivity assistant. Extract action items (tasks) from the provided content. " +
        "Return them as a JSON array of strings: [\"action 1\", \"action 2\"]. " +
        "If there are no action items, return []. Respond ONLY with valid JSON.";

    public static string DuplicateDetectSystem =>
        "You are a duplicate-detection assistant. Given a note and a list of existing notes, identify " +
        "which (if any) are likely duplicates or highly similar. Return a JSON array of IDs of the " +
        "duplicates: [\"id1\",\"id2\"]. Return [] if none. Respond ONLY with valid JSON.";

    public static string GenerateOutputSystem =>
        "You are a writing assistant helping create a polished document from research notes. Generate " +
        "clear, coherent output from the provided source notes. Preserve factual accuracy and cite key " +
        "ideas. Respond with plain text or markdown.";
}
