using Brainy.Application.DTOs.Para;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Provides aggregated PARA category counts for dashboard display.</summary>
public interface IParaSummaryService
{
    /// <summary>Returns item counts across all PARA categories for the current user.</summary>
    Task<ParaSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
