using System.Net;
using System.Text.Json;
using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Options;
using Microsoft.Extensions.Options;
using WebPush;

namespace Brainy.Application.Push;

/// <summary>
/// Sends Web Push messages via the <c>WebPush</c> library (VAPID signing + RFC 8291/8188
/// encryption). Selected by <c>DependencyInjection.AddWebPush</c> only when
/// <see cref="WebPushOptions.IsConfigured"/> is true; the VAPID private key never leaves this
/// class and is never logged.
/// </summary>
internal sealed class WebPushNotificationSender : IPushNotificationSender, IDisposable
{
    private readonly WebPushClient _client = new();
    private readonly VapidDetails _vapidDetails;

    public WebPushNotificationSender(IOptions<WebPushOptions> options)
    {
        var value = options.Value;
        _vapidDetails = new VapidDetails(value.VapidSubject, value.VapidPublicKey, value.VapidPrivateKey);
    }

    public async Task<PushSendResult> SendAsync(
        PushSubscriptionEndpointDto subscription,
        PushNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(payload);

        // Only the fixed heading/body/category ever go into the payload — see
        // PushNotificationPayload's remarks: this string is what leaves Brainy's
        // infrastructure via the browser vendor's push service.
        var payloadJson = JsonSerializer.Serialize(new
        {
            heading = payload.Heading,
            body = payload.Body,
            category = payload.Category?.ToString(),
        });

        var libSubscription = new WebPush.PushSubscription(
            subscription.Endpoint, subscription.P256dh, subscription.Auth);

        try
        {
            await _client.SendNotificationAsync(libSubscription, payloadJson, _vapidDetails, cancellationToken)
                .ConfigureAwait(false);
            return PushSendResult.Sent();
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // The push service has confirmed this endpoint no longer exists — the caller
            // must prune it rather than retry (issue #315's guardrail).
            return PushSendResult.Expired((int)ex.StatusCode);
        }
        catch (WebPushException ex)
        {
            return PushSendResult.Failed(ex.Message, (int)ex.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return PushSendResult.Failed(ex.Message);
        }
    }

    public void Dispose() => _client.Dispose();
}
