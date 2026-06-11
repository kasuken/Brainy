using Brainy.Application.DTOs.Search;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for full-text search across notes and outputs.</summary>
public interface ISearchService
{
    /// <summary>
    /// Searches notes and outputs owned by the current user by title and content.
    /// Results are ordered by relevance (title match &gt; content match),
    /// then by last-updated date descending.
    /// Use <see cref="Brainy.Application.DTOs.Search.SearchResultDto.ResultType"/> to
    /// distinguish "Note" from "Output" entries in the returned list.
    /// Returns an empty list when <paramref name="query"/> is blank.
    /// </summary>
    Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
