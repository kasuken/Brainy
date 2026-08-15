using System.Text.RegularExpressions;
using Brainy.Application.DTOs;
using Brainy.Application.DTOs.Search;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Searches notes, outputs, projects, areas, resources, tasks, goals, and ideas by title and content;
/// note tags also participate in matching and are returned for display.
/// for the current user. Results are ranked by relevance: title matches rank higher than
/// content-only matches, then sorted by last-updated date descending.
/// </summary>
internal sealed class SearchService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : ISearchService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int SnippetLength = 140;

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MarkdownImageRegex = new(@"!\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownFormattingRegex = new(@"(?m)^\s{0,3}(#{1,6}\s+|>\s+|[-*+]\s+)|(?<!\w)(\*\*|__|~~|`{1,3}|\*|_)(?!\w)", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public async Task<PagedResult<SearchResultDto>> SearchAsync(
        string query,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var term = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(term))
            return new PagedResult<SearchResultDto>([], 0, page, pageSize);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var fetchLimit = checked(page * pageSize);

        // One DB round-trip per entity type. EF Core translates String.Contains to LIKE '%term%'.
        // EF Core DbContext is not thread-safe: each query must complete before the next starts,
        // so these are awaited sequentially rather than run concurrently on the shared context.
        var notesQuery = context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId &&
                        !n.IsArchived &&
                        n.Status != NoteStatus.Archived &&
                        (n.Title.Contains(term) ||
                         n.Content.Contains(term) ||
                         n.Tags.Any(tag => tag.UserId == userId && tag.Name.Contains(term))));
        var notesCount = await notesQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var notes = await notesQuery
            .OrderByDescending(n => n.Title.Contains(term))
            .ThenByDescending(n => n.UpdatedAtUtc)
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
                Tags = n.Tags
                    .Where(tag => tag.UserId == userId)
                    .Select(tag => tag.Name)
                    .OrderBy(name => name)
                    .ToList(),
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputsQuery = context.Outputs
            .AsNoTracking()
            .Where(o => o.UserId == userId &&
                        !o.IsArchived &&
                        (o.Title.Contains(term) ||
                         (o.Description != null && o.Description.Contains(term)) ||
                         o.Content.Contains(term)));
        var outputsCount = await outputsQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var outputs = await outputsQuery
            .OrderByDescending(o => o.Title.Contains(term))
            .ThenByDescending(o => o.UpdatedAtUtc)
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
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projectsQuery = context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId &&
                        !p.IsArchived &&
                        (p.Name.Contains(term) ||
                         (p.Description != null && p.Description.Contains(term))));
        var projectsCount = await projectsQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var projects = await projectsQuery
            .OrderByDescending(p => p.Name.Contains(term))
            .ThenByDescending(p => p.UpdatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.AreaId,
                p.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var areasQuery = context.Areas
            .AsNoTracking()
            .Where(a => a.UserId == userId &&
                        !a.IsArchived &&
                        (a.Name.Contains(term) ||
                         (a.Description != null && a.Description.Contains(term))));
        var areasCount = await areasQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var areas = await areasQuery
            .OrderByDescending(a => a.Name.Contains(term))
            .ThenByDescending(a => a.UpdatedAtUtc)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Description,
                a.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var resourcesQuery = context.Resources
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                        !r.IsArchived &&
                        (r.Name.Contains(term) ||
                         (r.Description != null && r.Description.Contains(term)) ||
                         (r.Topic != null && r.Topic.Contains(term))));
        var resourcesCount = await resourcesQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var resources = await resourcesQuery
            .OrderByDescending(r => r.Name.Contains(term))
            .ThenByDescending(r => r.UpdatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.Topic,
                r.AreaId,
                r.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasksQuery = context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        !t.IsArchived &&
                        (t.Title.Contains(term) ||
                         (t.Description != null && t.Description.Contains(term))));
        var tasksCount = await tasksQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var tasks = await tasksQuery
            .OrderByDescending(t => t.Title.Contains(term))
            .ThenByDescending(t => t.UpdatedAtUtc)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Description,
                t.ProjectId,
                t.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goalsQuery = context.Goals
            .AsNoTracking()
            .Where(g => g.UserId == userId &&
                        !g.IsArchived &&
                        (g.Title.Contains(term) ||
                         (g.Description != null && g.Description.Contains(term))));
        var goalsCount = await goalsQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var goals = await goalsQuery
            .OrderByDescending(g => g.Title.Contains(term))
            .ThenByDescending(g => g.UpdatedAtUtc)
            .Select(g => new
            {
                g.Id,
                g.Title,
                g.Description,
                g.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ideasQuery = context.Ideas
            .AsNoTracking()
            .Where(i => i.UserId == userId &&
                        !i.IsArchived &&
                        (i.Title.Contains(term) ||
                         (i.Description != null && i.Description.Contains(term))));
        var ideasCount = await ideasQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var ideas = await ideasQuery
            .OrderByDescending(i => i.Title.Contains(term))
            .ThenByDescending(i => i.UpdatedAtUtc)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.Description,
                i.UpdatedAtUtc,
            })
            .Take(fetchLimit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Compute relevance in memory and merge all result sets.
        var noteResults = notes.Select(n =>
        {
            var titleMatch = n.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Content", n.Content),
                new SearchSnippetCandidate("Tags", BuildTagText(n.Tags)),
                new SearchSnippetCandidate("Title", n.Title));

            return new SearchResultDto(
                n.Id,
                n.Title,
                snippet.Text,
                n.AiSummary,
                n.Status,
                n.ParaCategory,
                n.ProjectId,
                n.AreaId,
                n.ResourceId,
                n.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Note",
                Tags: n.Tags,
                SnippetSource: snippet.Source);
        });

        var outputResults = outputs.Select(o =>
        {
            var titleMatch = o.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Content", o.Content),
                new SearchSnippetCandidate("Description", o.Description),
                new SearchSnippetCandidate("Title", o.Title));

            return new SearchResultDto(
                o.Id,
                o.Title,
                snippet.Text,
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
                OutputStatus: o.Status,
                SnippetSource: snippet.Source);
        });

        var projectResults = projects.Select(p =>
        {
            var titleMatch = p.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Description", p.Description),
                new SearchSnippetCandidate("Title", p.Name));

            return new SearchResultDto(
                p.Id,
                p.Name,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: p.AreaId,
                ResourceId: null,
                p.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Project",
                SnippetSource: snippet.Source);
        });

        var areaResults = areas.Select(a =>
        {
            var titleMatch = a.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Description", a.Description),
                new SearchSnippetCandidate("Title", a.Name));

            return new SearchResultDto(
                a.Id,
                a.Name,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                a.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Area",
                SnippetSource: snippet.Source);
        });

        var resourceResults = resources.Select(r =>
        {
            var titleMatch = r.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Topic", r.Topic),
                new SearchSnippetCandidate("Description", r.Description),
                new SearchSnippetCandidate("Title", r.Name));

            return new SearchResultDto(
                r.Id,
                r.Name,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: ParaCategory.Resource,
                ProjectId: null,
                AreaId: r.AreaId,
                ResourceId: r.Id,
                r.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Resource",
                SnippetSource: snippet.Source);
        });

        var taskResults = tasks.Select(t =>
        {
            var titleMatch = t.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Description", t.Description),
                new SearchSnippetCandidate("Title", t.Title));

            return new SearchResultDto(
                t.Id,
                t.Title,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: t.ProjectId,
                AreaId: null,
                ResourceId: null,
                t.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Task",
                SnippetSource: snippet.Source);
        });

        var goalResults = goals.Select(g =>
        {
            var titleMatch = g.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Description", g.Description),
                new SearchSnippetCandidate("Title", g.Title));

            return new SearchResultDto(
                g.Id,
                g.Title,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                g.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Goal",
                SnippetSource: snippet.Source);
        });

        var ideaResults = ideas.Select(i =>
        {
            var titleMatch = i.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
            var snippet = BuildSnippet(term,
                new SearchSnippetCandidate("Description", i.Description),
                new SearchSnippetCandidate("Title", i.Title));

            return new SearchResultDto(
                i.Id,
                i.Title,
                snippet.Text,
                AiSummary: null,
                Status: default,
                ParaCategory: default,
                ProjectId: null,
                AreaId: null,
                ResourceId: null,
                i.UpdatedAtUtc,
                Relevance: titleMatch ? 2 : 1,
                ResultType: "Idea",
                SnippetSource: snippet.Source);
        });

        var totalCount = notesCount + outputsCount + projectsCount + areasCount + resourcesCount + tasksCount + goalsCount + ideasCount;
        var items = noteResults
            .Concat(outputResults)
            .Concat(projectResults)
            .Concat(areaResults)
            .Concat(resourceResults)
            .Concat(taskResults)
            .Concat(goalResults)
            .Concat(ideaResults)
            .OrderByDescending(r => r.Relevance)
            .ThenByDescending(r => r.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<SearchResultDto>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Chooses the best field for a safe text snippet and extracts a short window
    /// around the first occurrence of <paramref name="term"/>.
    /// </summary>
    private static SearchSnippetInfo BuildSnippet(string term, params SearchSnippetCandidate[] candidates)
    {
        SearchSnippetInfo? fallback = null;

        foreach (var candidate in candidates)
        {
            var sanitized = SanitizeSnippetText(candidate.Text);
            if (string.IsNullOrWhiteSpace(sanitized))
                continue;

            fallback ??= new SearchSnippetInfo(ExtractSnippetWindow(sanitized, term), candidate.Source);

            if (sanitized.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return new SearchSnippetInfo(ExtractSnippetWindow(sanitized, term), candidate.Source);
        }

        return fallback ?? SearchSnippetInfo.Empty;
    }

    private static string ExtractSnippetWindow(string content, string term)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var idx = string.IsNullOrWhiteSpace(term)
            ? -1
            : content.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
            return content.Length <= SnippetLength
                ? content
                : content[..SnippetLength] + "…";

        var half = SnippetLength / 2;
        var start = Math.Max(0, idx - half);
        var end = Math.Min(content.Length, start + SnippetLength);
        start = Math.Max(0, end - SnippetLength);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < content.Length ? "…" : string.Empty;

        return prefix + content[start..end] + suffix;
    }

    private static string SanitizeSnippetText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = MarkdownImageRegex.Replace(text, "$1");
        sanitized = MarkdownLinkRegex.Replace(sanitized, "$1");
        sanitized = HtmlTagRegex.Replace(sanitized, " ");
        sanitized = MarkdownFormattingRegex.Replace(sanitized, string.Empty);
        sanitized = WhitespaceRegex.Replace(sanitized, " ").Trim();
        return sanitized;
    }

    private static string? BuildTagText(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return null;

        return string.Join(" · ", tags.Select(tag => $"#{tag}"));
    }

    private readonly record struct SearchSnippetCandidate(string Source, string? Text);

    private readonly record struct SearchSnippetInfo(string Text, string Source)
    {
        public static SearchSnippetInfo Empty => new(string.Empty, string.Empty);
    }
}
