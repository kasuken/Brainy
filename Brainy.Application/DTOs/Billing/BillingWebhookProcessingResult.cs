namespace Brainy.Application.DTOs.Billing;

/// <summary>Outcome of processing one inbound billing webhook delivery.</summary>
/// <param name="Accepted">
/// True when the request should be acknowledged (HTTP 2xx) — including an already-processed
/// duplicate, or a recognized-but-irrelevant event type. False means signature verification
/// failed and the request should be rejected.
/// </param>
/// <param name="Reason">A short machine-readable outcome code, for logging/audit.</param>
public sealed record BillingWebhookProcessingResult(bool Accepted, string Reason)
{
    public static readonly BillingWebhookProcessingResult InvalidSignature = new(false, "invalid_signature");
    public static readonly BillingWebhookProcessingResult Ignored = new(true, "ignored_event_type");
    public static readonly BillingWebhookProcessingResult AlreadyProcessed = new(true, "already_processed");
    public static readonly BillingWebhookProcessingResult Applied = new(true, "applied");
}
