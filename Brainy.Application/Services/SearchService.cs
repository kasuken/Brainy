using Brainy.Application.DTOs.Search;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Searches notes, outputs, projects, areas, tasks, goals, and ideas by title and content
/// for the current user. Results are ranked by relevance: title matches rank higher than
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

        // One DB round-trip per entity type. EF Core translates String.Contains to LIKE '%term%'.
        // EF Core DbContext is not thread-safe: each query must complete before the next starts,
        // so these are awaited sequentially rather than run concurrently on the shared context.
        var notes = await context.Notes
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
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputs = await context.Outputs
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
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projects = await context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId &&
                        !p.IsArchived &&
                        (p.Name.Contains(term) ||
                         (p.Description != null && p.Description.Contains(term))))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.AreaId,
                p.UpdatedAtUtc,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var areas = await context.Areas
            .AsNoTracking()
            .Where(a => a.UserId == userId &&
                        !a.IsArchived &&
                        (a.Name.Contains(term) ||
                         (a.Description != null && a.Description.Contains(term))))
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                a.UpdatedAtUtc,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        !t.IsArchived &&
                        (t.Title.Contains(term) ||
                         (t.Description != null && t.Description.Contains(term))))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Description,
                t.ProjectId,
                t.UpdatedAtUtc,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goals = await context.Goals
            .AsNoTracking()
            .Where(g => g.UserId == userId &&
                        !g.IsArchived &&
                        (g.Title.Contains(term) ||
                         (g.Description != null && g.Description.Contains(term))))
            .Select(g => new
            {
                g.Id,
                g.Title,
                g.Description,
                g.UpdatedAtUtc,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ideas = await context.Ideas
            .AsNoTracking()
            .Where(i => i.UserId == userId &&
                        !i.IsArchived &&
                        (i.Title.Contains(term) ||
                         (i.Description != null && i.Description.Contains(term))))
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Description,
                i.UpdatedAtUtc,
            })
            .Take(MaxResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Compute relevance in memory and merge all result sets.
        var noteResults = notes.Select(n =>
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

        var outputResults = outputs.Select(o =>
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

        var projectResults = projects.Select(p =>
        {
            var titleMatch = p.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                p.Id,
                p.Name,
                BuildSnippet(p.Description ?? string.Empty, term),
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: p.AreaId,
                ResourceId: null,
                p.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Project");
        });

        var areaResults = areas.Select(a =>
        {
            var titleMatch = a.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                a.Id,
                a.Name,
                BuildSnippet(a.Description ?? string.Empty, term),
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                a.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Area");
        });

        var taskResults = tasks.Select(t =>
        {
            var titleMatch = t.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                t.Id,
                t.Title,
                BuildSnippet(t.Description ?? string.Empty, term),
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: t.ProjectId,
                AreaId: null,
                ResourceId: null,
                t.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Task");
        });

        var goalResults = goals.Select(g =>
        {
            var titleMatch = g.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                g.Id,
                g.Title,
                BuildSnippet(g.Description ?? string.Empty, term),
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                g.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Goal");
        });

        var ideaResults = ideas.Select(i =>
        {
            var titleMatch = i.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            return new SearchResultDto(
                i.Id,
                i.Title,
                BuildSnippet(i.Description ?? string.Empty, term),
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                i.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Idea");
        });

        return noteResults
            .Concat(outputResults)
            .Concat(projectResults)
            .Concat(areaResults)
            .Concat(taskResults)
            .Concat(goalResults)
            .Concat(ideaResults)
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
