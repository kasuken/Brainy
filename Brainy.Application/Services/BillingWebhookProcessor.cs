using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
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
            return BillingWebhookProcessingResult.InvalidSignature;

        var parsed = await billingProvider.ParseWebhookEventAsync(payload, cancellationToken).ConfigureAwait(false);
        if (parsed is null)
            return BillingWebhookProcessingResult.Ignored;

        var alreadyProcessed = await context.ProcessedWebhookEvents.AsNoTracking()
            .AnyAsync(e => e.ProviderEventId == parsed.ProviderEventId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyProcessed)
            return BillingWebhookProcessingResult.AlreadyProcessed;

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
            // the race. Treat this one as the safe no-op it is instead of double-applying.
            return BillingWebhookProcessingResult.AlreadyProcessed;
        }

        if (!string.IsNullOrWhiteSpace(parsed.TargetUserId) && parsed.NewTier.HasValue)
        {
            await entitlements.SetPlanTierAsync(
                parsed.TargetUserId,
                parsed.NewTier.Value,
                parsed.PeriodEndsAtUtc,
                cancellationToken).ConfigureAwait(false);
        }

        return BillingWebhookProcessingResult.Applied;
    }
}
