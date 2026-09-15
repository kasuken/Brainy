using Brainy.Application.DTOs.Push;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages the current user's Web Push device subscriptions: registering a new device,
/// listing/removing existing ones, and sending an on-demand test notification. All members
/// are scoped to the authenticated user via <see cref="Identity.ICurrentUserService"/>.
/// </summary>
public interface IPushSubscriptionService
{
    /// <summary>
    /// Returns the VAPID public key browsers need to call <c>PushManager.subscribe()</c>, or
    /// null when no VAPID key pair is configured (push is unavailable on this deployment).
    /// </summary>
    string? GetVapidPublicKey();

    /// <summary>Lists the current user's registered devices, most recently created first.</summary>
    Task<IReadOnlyList<PushSubscriptionDto>> GetSubscriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers (or, if the endpoint already exists, re-registers in place) one device
    /// subscription for the current user.
    /// </summary>
    Task<PushSubscriptionDto> RegisterAsync(RegisterPushSubscriptionDto dto, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the current user's device subscriptions. A one-click, immediate unsubscribe.</summary>
    Task UnregisterAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an immediate test notification to every one of the current user's active
    /// subscriptions, bypassing category opt-in, quiet hours and the frequency cap (this is
    /// an explicit, one-off user action, not a scheduled trigger). Expired subscriptions
    /// encountered along the way are pruned. Returns true if at least one device accepted it.
    /// </summary>
    Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default);
}
