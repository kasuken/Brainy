using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Tests.Fakes;

/// <summary>
/// Records every send attempt (for content/frequency assertions) and lets tests script the
/// outcome per call via <see cref="OutcomeSelector"/> (default: always <see cref="PushSendResult.Sent"/>).
/// </summary>
public sealed class FakePushNotificationSender : IPushNotificationSender
{
    public List<(PushSubscriptionEndpointDto Subscription, PushNotificationPayload Payload)> Sent { get; } = [];

    public Func<PushSubscriptionEndpointDto, PushNotificationPayload, PushSendResult>? OutcomeSelector { get; set; }

    public Task<PushSendResult> SendAsync(
        PushSubscriptionEndpointDto subscription,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        Sent.Add((subscription, payload));
        var result = OutcomeSelector?.Invoke(subscription, payload) ?? PushSendResult.Sent();
        return Task.FromResult(result);
    }
}
