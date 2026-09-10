using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Billing;

/// <summary>Result of asking the billing provider to start a checkout flow.</summary>
/// <param name="Supported">False when no live payment provider is configured.</param>
/// <param name="RedirectUrl">The provider-hosted checkout URL, when supported.</param>
/// <param name="UnsupportedReason">A user-facing explanation when unsupported, for an honest CTA.</param>
public sealed record CheckoutSessionResult(bool Supported, string? RedirectUrl, string? UnsupportedReason);

/// <summary>Result of asking the billing provider to start a self-service billing-portal session.</summary>
public sealed record PortalSessionResult(bool Supported, string? RedirectUrl, string? UnsupportedReason);

/// <summary>Result of verifying an inbound webhook's signature.</summary>
public sealed record WebhookVerificationResult(bool IsValid, string? FailureReason);

/// <summary>A billing provider's webhook event, normalized to the fields Brainy's entitlement model needs.</summary>
/// <param name="ProviderEventId">The provider's unique event id, used for idempotency.</param>
/// <param name="EventType">The provider's raw event type string, kept for audit.</param>
/// <param name="TargetUserId">The Brainy user the event applies to, when the provider payload identifies one.</param>
/// <param name="NewTier">The plan tier to apply, when this event represents a plan change.</param>
/// <param name="PeriodEndsAtUtc">The new renewal/period-end timestamp, when the event carries one.</param>
public sealed record ParsedBillingWebhookEvent(
    string ProviderEventId,
    string EventType,
    string? TargetUserId,
    PlanTier? NewTier,
    DateTime? PeriodEndsAtUtc);
