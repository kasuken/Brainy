using Brainy.Application.DTOs.Push;

namespace Brainy.Application.Interfaces.Services;

/// <summary>How the push service responded to one delivery attempt.</summary>
public enum PushSendOutcome
{
    /// <summary>The push service accepted the message for delivery.</summary>
    Sent,

    /// <summary>
    /// The push service reported the subscription no longer exists (HTTP 404/410 —
    /// the user uninstalled, cleared site data, or the endpoint otherwise expired).
    /// The caller must prune this subscription rather than retry it.
    /// </summary>
    Expired,

    /// <summary>Delivery failed for any other reason (network error, 5xx, malformed keys). Not retried by this call.</summary>
    Failed
}

/// <summary>Result of one <see cref="IPushNotificationSender.SendAsync"/> call.</summary>
public record PushSendResult(PushSendOutcome Outcome, int? HttpStatusCode = null, string? Error = null)
{
    public static PushSendResult Sent() => new(PushSendOutcome.Sent);

    public static PushSendResult Expired(int httpStatusCode) => new(PushSendOutcome.Expired, httpStatusCode);

    public static PushSendResult Failed(string error, int? httpStatusCode = null) => new(PushSendOutcome.Failed, httpStatusCode, error);
}

/// <summary>
/// Sends one Web Push message to one subscribed device. Implemented in the Web layer (the
/// only place the VAPID private key and the chosen push library are wired up); the
/// Application layer only ever sees this seam, never the key or the library's types.
/// </summary>
public interface IPushNotificationSender
{
    /// <summary>
    /// Encrypts and delivers <paramref name="payload"/> to <paramref name="subscription"/>.
    /// Never throws for an ordinary delivery failure — the outcome is reported via
    /// <see cref="PushSendResult"/> so the caller can decide whether to prune the subscription.
    /// </summary>
    Task<PushSendResult> SendAsync(
        PushSubscriptionEndpointDto subscription,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default);
}
