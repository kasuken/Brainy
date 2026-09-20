using System.ComponentModel;
using Brainy.Application.Interfaces.Services;
using ModelContextProtocol.Server;

namespace Brainy.Web.Mcp.Tools;

/// <summary>
/// Read-only MCP tools over a signed-in user's Brainy knowledge base. Each tool is a thin
/// adapter over an existing application service — no business logic lives here. The user is
/// never a parameter: the MCP request is authenticated by <see cref="McpAuthenticationHandler"/>
/// into <c>HttpContext.User</c>, and the injected services resolve it through
/// <c>ICurrentUserService</c>, so every tool is automatically scoped to (and isolated to) the
/// caller's own data — the same per-user boundary the Blazor UI relies on.
/// </summary>
[McpServerToolType]
internal sealed class BrainyKnowledgeTools
{
    // Guards the server against an over-large page a client might request; the underlying
    // search service is unbounded, so the cap lives here at the tool boundary.
    private const int MaxPageSize = 50;

    [McpServerTool(Name = "search")]
    [Description(
        "Full-text search across the signed-in user's Brainy knowledge base: notes, outputs, " +
        "projects, areas, tasks, goals, and ideas. Returns the most relevant results first " +
        "(title matches rank above content matches). Use this to find what the user has " +
        "already captured before answering questions about their own knowledge or work.")]
    public static async Task<McpSearchResponse> SearchAsync(
        ISearchService searchService,
        [Description("The words or phrase to search for. An empty query returns no results.")]
        string query,
        [Description("1-based page number of results to return. Defaults to 1.")]
        int page = 1,
        [Description("Number of results per page (1-50). Defaults to 20.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var results = await searchService
            .SearchAsync(query ?? string.Empty, normalizedPage, normalizedPageSize, cancellationToken)
            .ConfigureAwait(false);

        var hits = results.Items
            .Select(item => new McpSearchHit(
                item.ResultType,
                item.Id,
                item.Title,
                item.ContentSnippet,
                item.AiSummary,
                item.UpdatedAtUtc))
            .ToList();

        return new McpSearchResponse(hits, results.TotalCount, results.Page, results.PageSize);
    }
}

/// <summary>A single search hit, projected for an LLM: identity, a snippet, and freshness.</summary>
public record McpSearchHit(
    [property: Description("The entity type: Note, Output, Project, Area, Task, Goal, or Idea.")]
    string Type,
    [property: Description("Stable identifier of the matched entity.")]
    Guid Id,
    [property: Description("Title or name of the matched entity.")]
    string Title,
    [property: Description("Short excerpt of the content around the match.")]
    string Snippet,
    [property: Description("AI-generated summary, when one exists for this entity.")]
    string? Summary,
    [property: Description("When the entity was last updated (UTC).")]
    DateTime UpdatedAtUtc);

/// <summary>A page of search hits plus the totals needed to page through more.</summary>
public record McpSearchResponse(
    [property: Description("The matching results for this page, most relevant first.")]
    IReadOnlyList<McpSearchHit> Results,
    [property: Description("Total number of results across all pages.")]
    int TotalCount,
    [property: Description("The 1-based page number these results are from.")]
    int Page,
    [property: Description("The number of results per page used for this response.")]
    int PageSize);
