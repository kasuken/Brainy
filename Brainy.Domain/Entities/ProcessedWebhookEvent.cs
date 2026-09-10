using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Idempotency ledger for inbound billing webhooks: records that a provider's event id has
/// already been applied, so at-least-once webhook delivery (retries) becomes a safe no-op
/// instead of re-applying a plan change. Also serves as the auditable record of billing
/// events received (issue #296 acceptance criterion: "auditable billing webhooks").
/// </summary>
public class ProcessedWebhookEvent : BaseEntity
{
    /// <summary>The billing provider's unique event id (e.g. Stripe's <c>evt_...</c> id).</summary>
    public string ProviderEventId { get; set; } = string.Empty;

    /// <summary>The provider's event type string (e.g. <c>customer.subscription.updated</c>), for audit.</summary>
    public string? EventType { get; set; }

    /// <summary>The Brainy user the event applied a plan change to, when known.</summary>
    public string? TargetUserId { get; set; }

    /// <summary>When this event was first (and only) applied.</summary>
    public DateTime ProcessedAtUtc { get; set; }
}
