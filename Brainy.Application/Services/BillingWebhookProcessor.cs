using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Telemetry;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Verifies and idempotently applies billing-provider webhook deliveries. Under
/// <c>NullBillingProvider</c>, <see cref="IBillingProvider.VerifyWebhookSignatureAsync"/>
/// always fails, so this never applies state without a real provider configured — there is
/// no way for an unauthenticated request to change anyone's plan.
/// </summary>
internal sealed class BillingWebhookProcessor(
    IBillingProvider billingProvider,
    IApplicationDbContext context,
    IEntitlementService entitlements,
    TimeProvider timeProvider) : IBillingWebhookProcessor
{
    public async Task<BillingWebhookProcessingResult> ProcessAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signatureHeader);

        var verification = await billingProvider
            .VerifyWebhookSignatureAsync(payload, signatureHeader, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.IsValid)
            return Record(BillingWebhookProcessingResult.InvalidSignature);

        var parsed = await billingProvider.ParseWebhookEventAsync(payload, cancellationToken).ConfigureAwait(false);
        if (parsed is null)
            return Record(BillingWebhookProcessingResult.Ignored);

        var alreadyProcessed = await context.ProcessedWebhookEvents.AsNoTracking()
            .AnyAsync(e => e.ProviderEventId == parsed.ProviderEventId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyProcessed)
            return Record(BillingWebhookProcessingResult.AlreadyProcessed);

        // State is applied before the event is marked processed, never the other way round.
        // The reverse order turns any failure below — a transient database fault, a deadlock —
        // into a permanently lost plan change: the provider's retry would find the event
        // already recorded and skip it, so a paying customer would silently stay on Starter.
        // Every operation below is idempotent (each is a no-op when the value already
        // matches), so a retry that re-applies part of an event is harmless.
        if (!string.IsNullOrWhiteSpace(parsed.TargetUserId))
        {
            if (parsed.BillingProviderCustomerId is not null || parsed.BillingProviderSubscriptionId is not null)
            {
                await entitlements.RecordBillingReferencesAsync(
                    parsed.TargetUserId,
                    parsed.BillingProviderCustomerId,
                    parsed.BillingProviderSubscriptionId,
                    cancellationToken).ConfigureAwait(false);
            }

            if (parsed.ClearsGracePeriod || parsed.GracePeriodEndsAtUtc.HasValue)
            {
                await entitlements.SetGracePeriodAsync(
                    parsed.TargetUserId,
                    parsed.ClearsGracePeriod ? null : parsed.GracePeriodEndsAtUtc,
                    cancellationToken).ConfigureAwait(false);
            }

            if (parsed.NewTier.HasValue)
            {
                await entitlements.SetPlanTierAsync(
                    parsed.TargetUserId,
                    parsed.NewTier.Value,
                    parsed.PeriodEndsAtUtc,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        context.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            Id = Guid.NewGuid(),
            ProviderEventId = parsed.ProviderEventId,
            EventType = parsed.EventType,
            TargetUserId = parsed.TargetUserId,
            ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique index on ProviderEventId: a concurrent delivery of the same event won
            // the race and has already applied the same idempotent changes.
            return Record(BillingWebhookProcessingResult.AlreadyProcessed);
        }

        return Record(BillingWebhookProcessingResult.Applied);
    }

    /// <summary>
    /// Records one processed webhook delivery, tagged only with the fixed <c>Reason</c> code
    /// (never the raw provider payload, target user id, or any billing identifier) before
    /// returning it unchanged.
    /// </summary>
    private static BillingWebhookProcessingResult Record(BillingWebhookProcessingResult result)
    {
        BrainyTelemetry.BillingWebhookEvents.Add(1,
            new KeyValuePair<string, object?>("billing.webhook_outcome", result.Reason));
        return result;
    }
}
