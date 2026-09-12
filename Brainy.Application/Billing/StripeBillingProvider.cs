using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using PlanTier = Brainy.Domain.Enums.PlanTier;

namespace Brainy.Application.Billing;

/// <summary>
/// Real <see cref="IBillingProvider"/> backed by Stripe hosted Checkout and the Customer
/// Portal, using the official Stripe.net SDK (never a hand-rolled HTTP call against the Stripe
/// REST API). Selected by <c>DependencyInjection.AddBilling</c> when <c>Billing:Provider</c> is
/// <see cref="BillingProviderType.Stripe"/>.
/// </summary>
/// <remarks>
/// Only Starter and Pro exist today, so the only checkout offered is an upgrade to Pro via a
/// single recurring price (<see cref="BillingOptions.ProPriceId"/>). The user id is threaded
/// through Stripe as the checkout session's <c>client_reference_id</c> and again as
/// <c>subscription_data.metadata["brainy_user_id"]</c>, so every subsequent subscription
/// webhook (created/updated/deleted) carries it without needing a customer-id lookup; invoice
/// events (which do not carry that metadata) resolve the user by looking up the stored Stripe
/// customer id instead.
/// </remarks>
internal sealed class StripeBillingProvider : IBillingProvider
{
    /// <summary>Stripe subscription/session metadata key carrying the Brainy user id.</summary>
    private const string UserIdMetadataKey = "brainy_user_id";

    /// <summary>
    /// Grace window applied when a charge fails and Stripe's invoice does not (yet) report a
    /// scheduled retry — the user keeps Pro access while Stripe's own retry schedule plays out.
    /// </summary>
    private static readonly TimeSpan DefaultPaymentFailureGracePeriod = TimeSpan.FromDays(7);

    private readonly BillingOptions _options;
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IStripeClient _stripeClient;

    public StripeBillingProvider(
        IOptions<BillingOptions> options,
        IApplicationDbContext context,
        TimeProvider timeProvider,
        IStripeClient? stripeClient = null)
    {
        _options = options.Value;
        _context = context;
        _timeProvider = timeProvider;
        _stripeClient = stripeClient ?? new StripeClient(_options.ApiKey);
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, PlanTier targetTier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        // Starter is free and needs no checkout; only an upgrade to Pro is ever purchasable.
        if (targetTier != PlanTier.Pro)
            return new CheckoutSessionResult(false, null, "Only upgrading to Pro requires checkout.");

        var existingCustomerId = await GetStoredCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);

        var sessionOptions = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "subscription",
            Customer = existingCustomerId,
            ClientReferenceId = userId,
            LineItems =
            [
                new Stripe.Checkout.SessionLineItemOptions { Price = _options.ProPriceId, Quantity = 1 },
            ],
            SuccessUrl = BuildAppUrl("/Account/Manage?billing=success"),
            CancelUrl = BuildAppUrl("/Account/Manage?billing=cancelled"),
            SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { [UserIdMetadataKey] = userId },
            },
        };

        var service = new Stripe.Checkout.SessionService(_stripeClient);
        var session = await service.CreateAsync(sessionOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(session.Url)
            ? new CheckoutSessionResult(false, null, "Stripe did not return a checkout URL.")
            : new CheckoutSessionResult(true, session.Url, null);
    }

    public async Task<PortalSessionResult> CreatePortalSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var customerId = await GetStoredCustomerIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customerId))
            return new PortalSessionResult(false, null, "You don't have a billing account yet. Upgrade to Pro first.");

        var service = new Stripe.BillingPortal.SessionService(_stripeClient);
        var session = await service.CreateAsync(
            new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = BuildAppUrl("/Account/Manage"),
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(session.Url)
            ? new PortalSessionResult(false, null, "Stripe did not return a portal URL.")
            : new PortalSessionResult(true, session.Url, null);
    }

    public Task<WebhookVerificationResult> VerifyWebhookSignatureAsync(
        string payload, string signatureHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSigningSecret))
            return Task.FromResult(new WebhookVerificationResult(false, "Webhook signing secret is not configured."));

        if (string.IsNullOrWhiteSpace(signatureHeader))
            return Task.FromResult(new WebhookVerificationResult(false, "Missing webhook signature header."));

        try
        {
            // Verification only: the constructed Event is discarded and re-parsed (unverified,
            // since it is by then known-good) in ParseWebhookEventAsync — the two are always
            // called in that order by BillingWebhookProcessor.
            EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSigningSecret);
            return Task.FromResult(new WebhookVerificationResult(true, null));
        }
        catch (StripeException ex)
        {
            return Task.FromResult(new WebhookVerificationResult(false, ex.Message));
        }
    }

    public async Task<ParsedBillingWebhookEvent?> ParseWebhookEventAsync(
        string payload, CancellationToken cancellationToken = default)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(payload);
        }
        catch (StripeException)
        {
            return null;
        }

        return stripeEvent.Type switch
        {
            "checkout.session.completed" => MapCheckoutSessionCompleted(stripeEvent),
            "customer.subscription.created" or "customer.subscription.updated" => MapSubscriptionUpdated(stripeEvent),
            "customer.subscription.deleted" => MapSubscriptionDeleted(stripeEvent),
            "invoice.payment_failed" => await MapInvoicePaymentFailedAsync(stripeEvent, cancellationToken).ConfigureAwait(false),
            "invoice.paid" => await MapInvoicePaidAsync(stripeEvent, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
    }

    private ParsedBillingWebhookEvent? MapCheckoutSessionCompleted(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session || session.Mode != "subscription")
            return null;

        var userId = session.ClientReferenceId;
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: PlanTier.Pro,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: session.CustomerId,
            BillingProviderSubscriptionId: session.SubscriptionId);
    }

    private ParsedBillingWebhookEvent? MapSubscriptionUpdated(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
            return null;

        var userId = ResolveUserIdFromMetadata(subscription.Metadata);
        if (userId is null)
            return null;

        // Active/trialing means Pro access is (still) in force; a period end is recorded so the
        // "Renews" date on the Plan & usage screen reflects it. Cancellation requested via the
        // portal only sets CancelAtPeriodEnd here — Stripe keeps the subscription active until
        // the period actually ends, at which point it fires customer.subscription.deleted.
        if (subscription.Status is "active" or "trialing")
        {
            var periodEnd = subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
            return new ParsedBillingWebhookEvent(
                ProviderEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                TargetUserId: userId,
                NewTier: PlanTier.Pro,
                PeriodEndsAtUtc: periodEnd,
                BillingProviderCustomerId: subscription.CustomerId,
                BillingProviderSubscriptionId: subscription.Id,
                ClearsGracePeriod: true);
        }

        // past_due/unpaid/incomplete etc.: don't change the tier here — invoice.payment_failed
        // records the grace period, and customer.subscription.deleted handles the eventual
        // downgrade once Stripe gives up. Still link the customer/subscription ids.
        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: null,
            PeriodEndsAtUtc: null,
            BillingProviderCustomerId: subscription.CustomerId,
            BillingProviderSubscriptionId: subscription.Id);
    }

    private ParsedBillingWebhookEvent? MapSubscriptionDeleted(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Subscription subscription)
            return null;

        var userId = ResolveUserIdFromMetadata(subscription.Metadata);
        if (userId is null)
            return null;

        return new ParsedBillingWebhookEvent(
            ProviderEventId: stripeEvent.Id,
            EventType: stripeEvent.Type,
            TargetUserId: userId,
            NewTier: PlanTier.Starter,
            PeriodEndsAtUtc: null,
            ClearsGracePeriod: true);
    }

    private async Task<ParsedBillingWebhookEvent?> MapInvoicePaymentFailedAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
            return null;

        var userId = await ResolveUserIdFromCustomerIdAsync(invoice.CustomerId, cancellationToken).ConfigureAwait(false);
        if (userId is null)
            return null;

        var gracePeriodEndsAtUtc = invoice.NextPaymentAttempt
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
            PeriodEndsAtUtc: invoice.PeriodEnd,
            ClearsGracePeriod: true);
    }

    private static string? ResolveUserIdFromMetadata(IDictionary<string, string>? metadata) =>
        metadata is not null && metadata.TryGetValue(UserIdMetadataKey, out var userId) && !string.IsNullOrWhiteSpace(userId)
            ? userId
            : null;

    private Task<string?> ResolveUserIdFromCustomerIdAsync(string? customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult<string?>(null);

        return _context.UserPlans.AsNoTracking()
            .Where(p => p.BillingProviderCustomerId == customerId)
            .Select(p => p.UserId)
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    private Task<string?> GetStoredCustomerIdAsync(string userId, CancellationToken cancellationToken) =>
        _context.UserPlans.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.BillingProviderCustomerId)
            .FirstOrDefaultAsync(cancellationToken)!;

    private string BuildAppUrl(string path) => $"{_options.AppBaseUrl?.TrimEnd('/')}{path}";
}
