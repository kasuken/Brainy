namespace Brainy.Application.Options;

/// <summary>
/// Web Push (VAPID) configuration, bound from the <c>WebPush</c> configuration section —
/// configuration only, never a database row or a client-supplied value (issue #315). All
/// three values default to empty, which the Web layer treats as "push not configured" and
/// registers a no-op <c>IPushNotificationSender</c> instead, the same graceful-default
/// pattern <c>BillingOptions</c>/<c>AiAssistantOptions</c> use.
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>A <c>mailto:</c> or <c>https:</c> contact URI, required by the VAPID spec so a push service can reach the sender.</summary>
    public string VapidSubject { get; set; } = string.Empty;

    /// <summary>URL-safe base64-encoded VAPID public key. Safe to send to the browser.</summary>
    public string VapidPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// URL-safe base64-encoded VAPID private key. Configuration only — never committed,
    /// never logged, and never sent to the client.
    /// </summary>
    public string VapidPrivateKey { get; set; } = string.Empty;

    /// <summary>True when all three values needed to sign and send a push are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VapidSubject) &&
        !string.IsNullOrWhiteSpace(VapidPublicKey) &&
        !string.IsNullOrWhiteSpace(VapidPrivateKey);
}
