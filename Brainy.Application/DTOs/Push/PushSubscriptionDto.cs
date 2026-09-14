namespace Brainy.Application.DTOs.Push;

/// <summary>One registered device, as shown on the push notification settings page.</summary>
public record PushSubscriptionDto(
    Guid Id,
    string? DeviceLabel,
    DateTime CreatedAtUtc,
    DateTime? LastSuccessAtUtc);
