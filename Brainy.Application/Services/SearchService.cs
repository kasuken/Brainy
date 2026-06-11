using Brainy.Application.DTOs.Search;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Searches notes and outputs by title and content for the current user.
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

        // Two parallel DB round-trips: one for Notes, one for Outputs.
        // EF Core translates String.Contains to LIKE '%term%'.
        var notesTask = context.Notes
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
            .ToListAsync(cancellationToken);

        var outputsTask = context.Outputs
            .AsNoTracking()
            .Where(o => o.UserId == userId &&
                        !o.IsArchived &&
                        (o.Title.Contains(term) ||
                         (o.Description != null && o.Description.Contains(term)) ||
                         o.Content.Contains(term)))
            .Select(o => new
            {
                o.Id,
                o.Title,
                o.Description,
                o.Content,
                o.Type,
                o.Status,
                o.UpdatedAtUtc,
            })
            .Take(MaxResults * 2)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(notesTask, outputsTask).ConfigureAwait(false);

        // Compute relevance in memory and merge both result sets.
        var noteResults = notesTask.Result.Select(n =>
        {
            var titleMatch = n.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                n.Id,
                n.Title,
                BuildSnippet(n.Content, term),
                n.AiSummary,
                n.Status,
                n.ParaCategory,
                n.ProjectId,
                n.AreaId,
                n.ResourceId,
                n.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Note");
        });

        var outputResults = outputsTask.Result.Select(o =>
        {
            var titleMatch = o.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(o.Content, term);
            // Fall back to description snippet when the content snippet is empty
            if (string.IsNullOrEmpty(snippet) && !string.IsNullOrEmpty(o.Description))
                snippet = BuildSnippet(o.Description, term);

            return new SearchResultDto(
                o.Id,
                o.Title,
                snippet,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                o.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Output",
                OutputType: o.Type,
                OutputStatus: o.Status);
        });

        return noteResults
            .Concat(outputResults)
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
