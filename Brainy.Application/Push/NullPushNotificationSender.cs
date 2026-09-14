using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Brainy.Application.Push;

/// <summary>
/// Safe no-op <see cref="IPushNotificationSender"/> used when no VAPID key pair is configured
/// (the default) — mirrors <see cref="Brainy.Application.Email.NullEmailSender"/> and
/// <see cref="Brainy.Application.Billing.NullBillingProvider"/>.
/// Reports every send as failed (never as sent, never as expired) so nothing gets pruned or
/// marked delivered when push simply is not set up on this deployment.
/// </summary>
internal sealed class NullPushNotificationSender(ILogger<NullPushNotificationSender> logger) : IPushNotificationSender
{
    public Task<PushSendResult> SendAsync(
        PushSubscriptionEndpointDto subscription,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("WebPush is not configured (no VAPID key pair); not sending \"{Heading}\".", payload.Heading);
        return Task.FromResult(PushSendResult.Failed("WebPush is not configured on this deployment."));
    }
}
