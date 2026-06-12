using Brainy.Application.AI.Prompts;
using Brainy.Application.Interfaces.AI;
using Brainy.Application.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Brainy.Application.AI;

/// <summary>
/// IAiAssistant implementation backed by an <see cref="IChatClient"/> (OpenAI or Azure OpenAI).
/// All methods follow the same pattern: build a system+user message pair, call the model, return
/// an <see cref="AiResult"/>. Exceptions are caught and converted to <see cref="AiResult.Failure"/>.
/// </summary>
internal sealed class OpenAiAssistant(IChatClient chatClient, IOptions<AiAssistantOptions> opts) : IAiAssistant
{
    private readonly AiAssistantOptions _options = opts.Value;

    /// <inheritdoc/>
    public async Task<AiResult> SummarizeAsync(string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, AiPrompts.SummarizeSystem),
                new(ChatRole.User, content),
            };
            var response = await chatClient.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);
            return new AiResult(response.Text, _options.Model, AiPrompts.SummarizeVersion, true);
        }
        catch (Exception ex)
        {
            return AiResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<AiResult> SuggestParaClassificationAsync(
        string title, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var userMessage = $"Title: {title}\n\nContent:\n{content}";
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, AiPrompts.ClassifySystem),
                new(ChatRole.User, userMessage),
            };
            var response = await chatClient.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);
            return new AiResult(response.Text, _options.Model, AiPrompts.ClassifyVersion, true);
        }
        catch (Exception ex)
        {
            return AiResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<AiResult> ExtractActionItemsAsync(string content, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, AiPrompts.ExtractActionsSystem),
                new(ChatRole.User, content),
            };
            var response = await chatClient.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);
            return new AiResult(response.Text, _options.Model, AiPrompts.ExtractActionsVersion, true);
        }
        catch (Exception ex)
        {
            return AiResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<AiResult> DetectDuplicatesAsync(
        string noteTitle,
        string noteContent,
        IReadOnlyList<(Guid Id, string Title, string? Summary)> candidates,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candidateList = string.Join("\n", candidates.Select(c =>
                $"- id: {c.Id}, title: \"{c.Title}\", summary: \"{c.Summary ?? "(none)"}\""));

            var userMessage =
                $"Note to check:\nTitle: {noteTitle}\nContent:\n{noteContent}\n\n" +
                $"Existing notes:\n{candidateList}";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, AiPrompts.DuplicateDetectSystem),
                new(ChatRole.User, userMessage),
            };
            var response = await chatClient.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);
            return new AiResult(response.Text, _options.Model, AiPrompts.DuplicateDetectVersion, true);
        }
        catch (Exception ex)
        {
            return AiResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<AiResult> GenerateOutputAsync(
        string outputTitle,
        string outputType,
        IReadOnlyList<string> sourceNoteContents,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceNotes = string.Join("\n\n---\n\n", sourceNoteContents.Select((c, i) =>
                $"Source note {i + 1}:\n{c}"));

            var userMessage =
                $"Output title: {outputTitle}\nOutput type: {outputType}\n\nSource notes:\n{sourceNotes}";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, AiPrompts.GenerateOutputSystem),
                new(ChatRole.User, userMessage),
            };
            var response = await chatClient.GetResponseAsync(messages, null, cancellationToken).ConfigureAwait(false);
            return new AiResult(response.Text, _options.Model, AiPrompts.GenerateOutputVersion, true);
        }
        catch (Exception ex)
        {
            return AiResult.Failure(ex.Message);
        }
    }
}
