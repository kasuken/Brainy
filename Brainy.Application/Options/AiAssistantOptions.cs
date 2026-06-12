namespace Brainy.Application.Options;

/// <summary>Configuration options for the AI assistant provider.</summary>
public sealed class AiAssistantOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "AiAssistant";

    /// <summary>The AI provider to use. Defaults to <see cref="AiProviderType.None"/> (AI disabled).</summary>
    public AiProviderType Provider { get; set; } = AiProviderType.None;

    /// <summary>API key for the chosen provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com/</summary>
    public string? Endpoint { get; set; }

    /// <summary>Azure OpenAI deployment name. Falls back to <see cref="Model"/> when not set.</summary>
    public string? DeploymentName { get; set; }

    /// <summary>Model identifier sent with every request. Defaults to gpt-4o-mini.</summary>
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>Supported AI provider back-ends.</summary>
public enum AiProviderType
{
    /// <summary>AI features are disabled. All calls return a graceful failure message.</summary>
    None,

    /// <summary>OpenAI public API (api.openai.com).</summary>
    OpenAI,

    /// <summary>Azure OpenAI Service.</summary>
    AzureOpenAI,
}
