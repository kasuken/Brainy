using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Options;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

// Stripe.net ships its own unrelated PlanTier (tiered-pricing metadata on legacy Plans),
// which collides with Brainy's plan enum in this file's scope.
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Application.Billing;

/// <summary>
/// Live Stripe implementation of <see cref="IBillingProvider"/>, selected by
/// <c>Billing:Provider = Stripe</c>. Upgrades go through hosted Stripe Checkout, self-service
/// changes through the hosted Customer Portal, and every plan-state change arrives back as a
/// signature-verified webhook — Brainy never grants a tier from a browser redirect, only from
/// a verified event, so a user who edits a success URL cannot upgrade themselves.
/// </summary>
/// <remarks>
/// <para>
/// The Brainy user id travels to Stripe in three places, because each read path sees a
/// different object: <c>client_reference_id</c> and <c>metadata</c> on the Checkout Session,
/// and <c>metadata</c> on the Subscription it creates. Subscription events therefore identify
/// their Brainy user without an extra API call; when metadata is missing (for example a
/// subscription created by hand in the Stripe Dashboard) the customer id is matched against
/// <see cref="Domain.Entities.UserPlan.BillingProviderCustomerId"/> instead.
/// </para>
/// <para>
/// Tier is resolved from the subscription's price id against the configured Pro prices, so a
/// price that exists in Stripe but is not configured here resolves to no paid tier rather
/// than silently granting Pro.
/// </para>
/// </remarks>
internal sealed class StripeBillingProvider(
    IOptions<BillingOptions> options,
    IApplicationDbContext context,
    ILogger<StripeBillingProvider> logger) : IBillingProvider
{
    /// <summary>Session/subscription metadata key carrying the Brainy user id.</summary>
    internal const string UserIdMetadataKey = "brainy_user_id";

    /// <summary>
    /// Stripe subscription statuses that still entitle the user to their paid tier.
    /// <c>past_due</c> is included deliberately: Stripe retries a failed charge for days
    /// before giving up, and cutting access off on the first failure would punish an expired
    /// card. The terminal outcome arrives as <c>customer.subscription.deleted</c>.
    /// </summary>
    private static readonly string[] EntitlingStatuses = ["active", "trialing", "past_due"];

    private readonly BillingOptions _options = options.Value;
    private readonly IStripeClient _stripe = new StripeClient(options.Value.ApiKey);

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        PlanTier targetTier,
        BillingInterval interval = BillingInterval.Yearly,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (targetTier == PlanTier.Starter)
        {
            // Starter is free and has no Stripe price: moving down to it is a cancellation,
            // which belongs in the portal, not in a checkout session for a $0 product.
            return new CheckoutSessionResult(false, null,
                "Starter is free — cancel your Pro subscription from the billing portal to move back to it.");
        }

        var priceId = ResolvePriceId(targetTier, interval);
        var existingCustomerId = await GetCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = _options.CheckoutSuccessUrl,
            CancelUrl = _options.CheckoutCancelUrl,

            // Identifies the Brainy user on checkout.session.completed.
            ClientReferenceId = userId,
            Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId },

            // Reuse the existing customer when we have one, so a returning user doesn't end
            // up with a second Stripe customer record; otherwise let Stripe create one.
            Customer = existingCustomerId,

            // Copied onto the created Subscription, so later subscription.* events identify
            // their Brainy user without a lookup.
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId },
            },
        };

        try
        {
            var session = await new SessionService(_stripe)
                .CreateAsync(sessionOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new CheckoutSessionResult(true, session.Url, null);
        }
        catch (StripeException ex)
        {
            // Stripe's own message can name prices, accounts, or API keys; log it and show
            // the user a generic retry instead.
            logger.LogError(ex, "Stripe checkout session creation failed for user {UserId} on price {PriceId}.", userId, priceId);
            return new CheckoutSessionResult(false, null,
                "We couldn't start checkout just now. Please try again in a moment.");
        }
    }

    public async Task<PortalSessionResult> CreatePortalSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var customerId = await GetCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customerId))
        {
            // No Stripe customer yet: the user has never completed a checkout, so there is
            // nothing for the portal to manage.
            return new PortalSessionResult(false, null,
                "You don't have a paid subscription yet, so there's nothing to manage here.");
        }

        try
        {
            var session = await new Stripe.BillingPortal.SessionService(_stripe)
                .CreateAsync(
                    new Stripe.BillingPortal.SessionCreateOptions
                    {
                        Customer = customerId,
                        ReturnUrl = _options.PortalReturnUrl,
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new PortalSessionResult(true, session.Url, null);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe billing portal session creation failed for user {UserId}.", userId);
            return new PortalSessionResult(false, null,
                "We couldn't open the billing portal just now. Please try again in a moment.");
        }
    }

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload, string signatureHeader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signatureHeader);

        try
        {
            // Also enforces Stripe's replay-window tolerance on the signed timestamp.
            // throwOnApiVersionMismatch: false — Brainy reads only long-stable fields, and a
            // Stripe-initiated API version bump must not start rejecting live webhooks.
            EventUtility.ConstructEvent(
                payload,
                signatureHeader,
                _options.WebhookSigningSecret,
                throwOnApiVersionMismatch: false);

            return Task.FromResult(new WebhookVerificationResult(true, null));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Rejected a billing webhook delivery with an invalid Stripe signature.");
            return Task.FromResult(new WebhookVerificationResult(false, "Stripe signature verification failed."));
        }
    }

    public async Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(
        string payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Event stripeEvent;
        try
        {
            // The signature was already verified by VerifyWebhookSignatureAsync; this only
            // deserializes the payload the processor already trusts.
            stripeEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Could not parse a signature-verified Stripe webhook payload.");
            return null;
        }

        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted =>
                ParseCheckoutCompleted(stripeEvent),

            EventTypes.CustomerSubscriptionCreated or
            EventTypes.CustomerSubscriptionUpdated or
            EventTypes.CustomerSubscriptionDeleted =>
                await ParseSubscriptionEventAsync(stripeEvent, cancellationToken).ConfigureAwait(false),

            // Every other event type (invoice.*, payment_intent.*, ...) carries nothing
            // Brainy's entitlement model acts on. Returning null makes the processor
            // acknowledge the delivery so Stripe stops retrying it.
            _ => null,
        };
    }

    /// <summary>
    /// Links the Stripe customer and subscription to the Brainy user. No tier is granted
    /// here: the subscription events carry the authoritative price and period, and Stripe
    /// always sends one alongside a completed checkout.
    /// </summary>
    private ParsedBillingWebhookEvent? ParseCheckoutCompleted(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Session session)
            return null;

        var userId = session.ClientReferenceId ?? GetMetadataUserId(session.Metadata);
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning(
                "Stripe checkout session {SessionId} completed without a Brainy user id; ignoring.", session.Id);
            return null;
        }

        return new ParsedBillingWebhookEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            ProviderCustomerId: session.CustomerId,
            ProviderSubscriptionId: session.SubscriptionId);
    }

    private async Task<ParsedBillingWebhookEvent?> ParseSubscriptionEventAsync(
        Event stripeEvent, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
            return null;

        var userId = GetMetadataUserId(subscription.Metadata)
            ?? await FindUserIdByCustomerAsync(subscription.CustomerId, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning(
                "Stripe subscription {SubscriptionId} for customer {CustomerId} maps to no Brainy user; ignoring.",
                subscription.Id, subscription.CustomerId);
            return null;
        }

        var deleted = stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted;
        var entitled = !deleted && EntitlingStatuses.Contains(subscription.Status);

        // A cancelled, unpaid, or incomplete subscription drops the user to the free tier.
        // Resolving Pro from the price id (rather than assuming it) means an unconfigured
        // price can never silently grant a paid tier.
        var tier = entitled ? ResolveTier(subscription) : PlanTier.Starter;

        return new ParsedBillingWebhookEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            userId,
            tier,
            entitled ? GetCurrentPeriodEndUtc(subscription) : null,
            subscription.CustomerId,
            subscription.Id);
    }

    /// <summary>
    /// Maps a subscription's price ids back to a Brainy tier. An unrecognized price falls
    /// back to <see cref="PlanTier.Starter"/> with a warning rather than granting a tier
    /// Brainy has no entitlement row for.
    /// </summary>
    private PlanTier ResolveTier(Subscription subscription)
    {
        var priceIds = subscription.Items?.Data?
            .Select(item => item.Price?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList() ?? [];

        if (priceIds.Any(id => id == _options.ProMonthlyPriceId || id == _options.ProYearlyPriceId))
            return PlanTier.Pro;

        logger.LogWarning(
            "Stripe subscription {SubscriptionId} carries no configured Brainy price ({PriceIds}); treating as Starter.",
            subscription.Id, string.Join(", ", priceIds));
        return PlanTier.Starter;
    }

    /// <summary>
    /// Reads the current period end. As of Stripe API version 2025-03-31 this lives on the
    /// subscription's items rather than on the subscription itself, so the latest end across
    /// items is used: Brainy sells a single-item subscription, but taking the maximum is
    /// correct for any subscription and never returns a stale date.
    /// </summary>
    private static DateTime? GetCurrentPeriodEndUtc(Subscription subscription)
    {
        var periodEnds = subscription.Items?.Data?
            .Select(item => item.CurrentPeriodEnd)
            .Where(end => end != default)
            .ToList();

        return periodEnds is { Count: > 0 } ? periodEnds.Max().ToUniversalTime() : null;
    }

    private string ResolvePriceId(PlanTier tier, BillingInterval interval) => (tier, interval) switch
    {
        (PlanTier.Pro, BillingInterval.Monthly) => _options.ProMonthlyPriceId!,
        (PlanTier.Pro, BillingInterval.Yearly) => _options.ProYearlyPriceId!,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "No Stripe price is configured for this plan tier."),
    };

    private static string? GetMetadataUserId(IDictionary<string, string>? metadata) =>
        metadata is not null && metadata.TryGetValue(UserIdMetadataKey, out var userId) && !string.IsNullOrWhiteSpace(userId)
            ? userId
            : null;

    private Task<string?> GetCustomerIdAsync(string userId, CancellationToken cancellationToken) =>
        context.UserPlans.AsNoTracking()
            .Where(plan => plan.UserId == userId)
            .Select(plan => plan.BillingProviderCustomerId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<string?> FindUserIdByCustomerAsync(string? customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return null;

        return await context.UserPlans.AsNoTracking()
            .Where(plan => plan.BillingProviderCustomerId == customerId)
            .Select(plan => plan.UserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
