using Brainy.Application.DTOs.Search;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Application service for full-text search across all Brainy entity types.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches notes, outputs, projects, areas, tasks, goals, and ideas owned by the
    /// current user by title and content. Results are ordered by relevance (title match
    /// &gt; content match), then by last-updated date descending.
    /// Use <see cref="Brainy.Application.DTOs.Search.SearchResultDto.ResultType"/> to
    /// distinguish result types: "Note", "Output", "Project", "Area", "Task", "Goal", "Idea".
    /// Returns an empty list when <paramref name="query"/> is blank.
    /// </summary>
    Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
