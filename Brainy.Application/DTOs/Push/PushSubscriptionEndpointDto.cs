namespace Brainy.Application.DTOs.Push;

/// <summary>
/// The minimal, provider-agnostic shape <see cref="Interfaces.Services.IPushNotificationSender"/>
/// needs to deliver to one device: the push service endpoint and the subscription's own
/// encryption keys. Deliberately excludes <see cref="Domain.Entities.PushSubscription"/>'s
/// audit/tracking fields so the sender abstraction never needs to know about the entity.
/// </summary>
public record PushSubscriptionEndpointDto(string Endpoint, string P256dh, string Auth);
