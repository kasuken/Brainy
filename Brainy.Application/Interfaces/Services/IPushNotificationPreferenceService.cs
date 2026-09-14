using Brainy.Application.DTOs.Push;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages the current user's push notification preferences (master opt-in, per-category
/// toggles, quiet hours, schedule). A default (master switch off) record is created on first
/// access if none exists — a user who never opts in has no row, treated as disabled.
/// </summary>
public interface IPushNotificationPreferenceService
{
    Task<PushNotificationPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task<PushNotificationPreferenceDto> UpdateAsync(
        UpdatePushNotificationPreferenceDto dto,
        CancellationToken cancellationToken = default);
}
