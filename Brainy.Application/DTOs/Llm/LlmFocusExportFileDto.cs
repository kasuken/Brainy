namespace Brainy.Application.DTOs.Llm;

/// <summary>A focused, versioned Brainy snapshot ready for an external LLM.</summary>
public sealed record LlmFocusExportFileDto(
    string FileName,
    string ContentType,
    string SchemaVersion,
    string PromptVersion,
    byte[] Content);
