using Brainy.Application.DTOs.Today;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Detects inconsistencies between a project's status and the status of the tasks it
/// contains (for example, in-progress work in a non-active project, or an archived
/// project that still has open tasks) and surfaces them for the Today screen.
/// </summary>
public interface IStatusConsistencyService
{
    /// <summary>
    /// Returns the current user's status inconsistencies, ordered most important first.
    /// Returns an empty list when everything is consistent.
    /// </summary>
    Task<IReadOnlyList<StatusConsistencyWarningDto>> GetWarningsAsync(
        CancellationToken cancellationToken = default);
}
