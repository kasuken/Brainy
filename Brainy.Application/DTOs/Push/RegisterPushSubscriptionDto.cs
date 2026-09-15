namespace Brainy.Application.DTOs.Push;

/// <summary>
/// The raw <c>PushSubscription</c> JS object (endpoint + keys) handed back by
/// <c>PushManager.subscribe()</c> in the browser, plus an optional device label.
/// </summary>
public record RegisterPushSubscriptionDto(
    string Endpoint,
    string P256dh,
    string Auth,
    string? DeviceLabel);
