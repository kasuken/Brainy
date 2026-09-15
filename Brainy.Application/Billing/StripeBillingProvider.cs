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
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Application.Billing;

/// <summary>
/// Live Stripe implementation of <see cref="IBillingProvider"/>, selected by
/// <c>Billing:Provider = Stripe</c>. Upgrades go through hosted Stripe Checkout, self-service
/// changes through the hosted Customer Portal, and plan-state changes are applied only from
/// signature-verified webhooks.
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
internal sealed class StripeBillingProvider : IBillingProvider
{
    internal const string UserIdMetadataKey = "brainy_user_id";

    private static readonly string[] EntitlingStatuses = ["active", "trialing", "past_due"];
    private static readonly TimeSpan DefaultPaymentFailureGracePeriod = TimeSpan.FromDays(7);

    private readonly BillingOptions _options;
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StripeBillingProvider> _logger;
    private readonly IStripeClient _stripeClient;

    public StripeBillingProvider(
        IOptions<BillingOptions> options,
        IApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<StripeBillingProvider> logger,
        IStripeClient? stripeClient = null)
    {
        _options = options.Value;
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
        _stripeClient = stripeClient ?? new StripeClient(_options.ApiKey);
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId,
        PlanTier targetTier,
        BillingInterval interval = BillingInterval.Yearly,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (targetTier != PlanTier.Pro)
            return new CheckoutSessionResult(false, null, "Only upgrading to Pro requires checkout.");

        var priceId = ResolvePriceId(targetTier, interval);
        var existingCustomerId = await GetStoredCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);

        var sessionOptions = new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = existingCustomerId,
            ClientReferenceId = userId,
            Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId },
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            SuccessUrl = _options.CheckoutSuccessUrl,
            CancelUrl = _options.CheckoutCancelUrl,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId },
            },
        };

        try
        {
            var session = await new SessionService(_stripeClient)
                .CreateAsync(sessionOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(session.Url)
                ? new CheckoutSessionResult(false, null, "Stripe did not return a checkout URL.")
                : new CheckoutSessionResult(true, session.Url, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe checkout session creation failed for user {UserId} on price {PriceId}.", userId, priceId);
            return new CheckoutSessionResult(false, null, "We couldn't start checkout just now. Please try again in a moment.");
        }
    }

    public async Task<PortalSessionResult> CreatePortalSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var customerId = await GetStoredCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customerId))
            return new PortalSessionResult(false, null, "You don't have a billing account yet. Upgrade to Pro first.");

        try
        {
            var session = await new Stripe.BillingPortal.SessionService(_stripeClient)
                .CreateAsync(
                    new Stripe.BillingPortal.SessionCreateOptions
                    {
                        Customer = customerId,
                        ReturnUrl = _options.PortalReturnUrl,
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(session.Url)
                ? new PortalSessionResult(false, null, "Stripe did not return a portal URL.")
                : new PortalSessionResult(true, session.Url, null);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe billing portal session creation failed for user {UserId}.", userId);
            return new PortalSessionResult(false, null, "We couldn't open the billing portal just now. Please try again in a moment.");
        }
    }

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(_options.WebhookSigningSecret))
            return Task.FromResult(new WebhookVerificationResult(false, "Webhook signing secret is not configured."));

        if (string.IsNullOrWhiteSpace(signatureHeader))
            return Task.FromResult(new WebhookVerificationResult(false, "Missing webhook signature header."));

        try
        {
            EventUtility.ConstructEvent(
                payload,
                signatureHeader,
                _options.WebhookSigningSecret,
                throwOnApiVersionMismatch: false);

            return Task.FromResult(new WebhookVerificationResult(true, null));
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected a billing webhook delivery with an invalid Stripe signature.");
            return Task.FromResult(new WebhookVerificationResult(false, "Stripe signature verification failed."));
        }
    }

    public async Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not parse a signature-verified Stripe webhook payload.");
            return null;
        }

        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted => ParseCheckoutCompleted(stripeEvent),
            EventTypes.CustomerSubscriptionCreated or EventTypes.CustomerSubscriptionUpdated or EventTypes.CustomerSubscriptionDeleted
                => await ParseSubscriptionEventAsync(stripeEvent, cancellationToken).ConfigureAwait(false),
            EventTypes.InvoicePaymentFailed => await MapInvoicePaymentFailedAsync(stripeEvent, cancellationToken).ConfigureAwait(false),
            EventTypes.InvoicePaid => await MapInvoicePaidAsync(stripeEvent, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
    }

    private ParsedBillingWebhookEvent? ParseCheckoutCompleted(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Session session || session.Mode != "subscription")
            return null;

        var userId = session.ClientReferenceId ?? GetMetadataUserId(session.Metadata);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Stripe checkout session {SessionId} completed without a Brainy user id; ignoring.", session.Id);
            return null;
        }

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: session.CustomerId,
            BillingProviderSubscriptionId: session.SubscriptionId);
    }

    private async Task<ParsedBillingWebhookEvent?> ParseSubscriptionEventAsync(
        Event stripeEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
            return null;

        var userId = GetMetadataUserId(subscription.Metadata)
            ?? await ResolveUserIdFromCustomerIdAsync(subscription.CustomerId, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning(
                "Stripe subscription {SubscriptionId} for customer {CustomerId} maps to no Brainy user; ignoring.",
                subscription.Id,
                subscription.CustomerId);
            return null;
        }

        if (stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted)
        {
            return new ParsedBillingWebhookEvent(
                ProviderEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                TargetUserId: userId,
                NewTier: PlanTier.Starter,
                PeriodEndsAtUtc: null,
                BillingProviderCustomerId: subscription.CustomerId,
                BillingProviderSubscriptionId: subscription.Id,
                ClearsGracePeriod: true);
        }

        if (EntitlingStatuses.Contains(subscription.Status))
        {
            return new ParsedBillingWebhookEvent(
                ProviderEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                TargetUserId: userId,
                NewTier: ResolveTier(subscription),
                PeriodEndsAtUtc: GetCurrentPeriodEndUtc(subscription),
                BillingProviderCustomerId: subscription.CustomerId,
                BillingProviderSubscriptionId: subscription.Id,
                ClearsGracePeriod: subscription.Status is "active" or "trialing");
        }

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: subscription.CustomerId,
            BillingProviderSubscriptionId: subscription.Id);
    }

    private async Task<ParsedBillingWebhookEvent?> MapInvoicePaymentFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
            return null;

        var userId = await ResolveUserIdFromCustomerIdAsync(invoice.CustomerId, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return null;

        var gracePeriodEndsAtUtc = invoice.NextPaymentAttempt?.ToUniversalTime()
            ?? _timeProvider.GetUtcNow().UtcDateTime + DefaultPaymentFailureGracePeriod;

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            GracePeriodEndsAtUtc: gracePeriodEndsAtUtc);
    }

    private async Task<ParsedBillingWebhookEvent?> MapInvoicePaidAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
            return null;

        var userId = await ResolveUserIdFromCustomerIdAsync(invoice.CustomerId, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return null;

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: PlanTier.Pro,
            PeriodEndsAtUtc: invoice.PeriodEnd.ToUniversalTime(),
            ClearsGracePeriod: true);
    }

    private PlanTier ResolveTier(Subscription subscription)
    {
        var priceIds = subscription.Items?.Data?
            .Select(item => item.Price?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList() ?? [];

        if (priceIds.Any(id => id == _options.ProMonthlyPriceId || id == _options.ProYearlyPriceId))
            return PlanTier.Pro;

        _logger.LogWarning(
            "Stripe subscription {SubscriptionId} carries no configured Brainy price ({PriceIds}); treating as Starter.",
            subscription.Id,
            string.Join(", ", priceIds));
        return PlanTier.Starter;
    }

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

    private Task<string?> ResolveUserIdFromCustomerIdAsync(string? customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult<string?>(null);

        return _context.UserPlans.AsNoTracking()
            .Where(plan => plan.BillingProviderCustomerId == customerId)
            .Select(plan => plan.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<string?> GetStoredCustomerIdAsync(string userId, CancellationToken cancellationToken) =>
        _context.UserPlans.AsNoTracking()
            .Where(plan => plan.UserId == userId)
            .Select(plan => plan.BillingProviderCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
}
