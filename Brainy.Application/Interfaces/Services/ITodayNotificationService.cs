using Brainy.Application.DTOs.Today;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Evaluates the user's current work state and emits actionable notifications for the Today screen.
/// </summary>
public interface ITodayNotificationService
{
    /// <summary>Returns active notifications based on the user's current work state.</summary>
    Task<IReadOnlyList<TodayNotificationDto>> GetNotificationsAsync(CancellationToken cancellationToken = default);
}
