using Brainy.Application.DTOs.Search;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Searches notes by title and content for the current user.
/// Results are ranked by relevance: title matches rank higher than
/// content-only matches, then sorted by last-updated date descending.
/// </summary>
internal sealed class SearchService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : ISearchService
{
    private const int MaxResults = 100;
    private const int SnippetLength = 200;

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var term = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
            return [];

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Single DB round-trip: filter by title OR content, then sort by relevance in memory.
        // EF Core translates String.Contains to LIKE '%term%' on SQL Server.
        var matches = await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId &&
                        (n.Title.Contains(term) || n.Content.Contains(term)))
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Content,
                n.AiSummary,
                n.Status,
                n.ParaCategory,
                n.ProjectId,
                n.AreaId,
                n.ResourceId,
                n.UpdatedAtUtc,
            })
            .Take(MaxResults * 2) // over-fetch before in-memory sort
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Compute relevance in memory and sort: title match first, then most recent.
        return matches
            .Select(n =>
            {
                var titleMatch = n.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
                var relevance = titleMatch ? 2 : 1;
                var snippet = BuildSnippet(n.Content, term);
                return new SearchResultDto(
                    n.Id,
                    n.Title,
                    snippet,
                    n.AiSummary,
                    n.Status,
                    n.ParaCategory,
                    n.ProjectId,
                    n.AreaId,
                    n.ResourceId,
                    n.UpdatedAtUtc,
                    relevance);
            })
            .OrderByDescending(r => r.Relevance)
            .ThenByDescending(r => r.UpdatedAtUtc)
            .Take(MaxResults)
            .ToList();
    }

    /// <summary>
    /// Extracts a snippet of up to <see cref="SnippetLength"/> characters centred
    /// around the first occurrence of <paramref name="term"/> in the content.
    /// Falls back to the opening of the content when the term is not found.
    /// </summary>
    private static string BuildSnippet(string content, string term)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            // Term not in content — show the beginning
            return content.Length <= SnippetLength
                ? content
                : content[..SnippetLength] + "…";

        // Centre the window around the match
        var half = SnippetLength / 2;
        var start = Math.Max(0, idx - half);
        var end = Math.Min(content.Length, start + SnippetLength);

        // Adjust start when the end is capped
        start = Math.Max(0, end - SnippetLength);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < content.Length ? "…" : string.Empty;

        return prefix + content[start..end] + suffix;
    }
}
