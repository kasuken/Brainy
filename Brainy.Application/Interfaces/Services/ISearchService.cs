using Brainy.Application.DTOs.Search;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for full-text search across notes.</summary>
public interface ISearchService
{
    /// <summary>
    /// Searches notes owned by the current user by title and content.
    /// Results are ordered by relevance (title match &gt; content match),
    /// then by last-updated date descending.
    /// Returns an empty list when <paramref name="query"/> is blank.
    /// </summary>
    Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
