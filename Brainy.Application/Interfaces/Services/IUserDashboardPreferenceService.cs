using Brainy.Application.DTOs.Dashboard;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages per-user dashboard layout and threshold preferences.
/// A preference record is created on first access if one does not yet exist.
/// </summary>
public interface IUserDashboardPreferenceService
{
    /// <summary>
    /// Returns the current user's dashboard preferences, creating a default record if none exists.
    /// </summary>
    Task<UserDashboardPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's dashboard preferences and returns the updated record.
    /// </summary>
    Task<UserDashboardPreferenceDto> UpdateAsync(UpdateDashboardPreferenceDto dto, CancellationToken cancellationToken = default);
}
