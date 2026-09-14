using System.Security.Cryptography;
using System.Text;
using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Options;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IPushSubscriptionService"/>. The endpoint is hashed for the unique
/// index the same way <c>CalendarFeedTokenService</c> hashes its token (see
/// <see cref="PushSubscription.EndpointHash"/>); the raw endpoint and keys are kept because,
/// unlike a bearer token, they must be replayed to the push service on every send.
/// </summary>
internal sealed class PushSubscriptionService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IPushNotificationSender sender,
    IOptions<WebPushOptions> webPushOptions,
    TimeProvider timeProvider) : IPushSubscriptionService
{
    public string? GetVapidPublicKey() =>
        webPushOptions.Value.IsConfigured ? webPushOptions.Value.VapidPublicKey : null;

    public async Task<IReadOnlyList<PushSubscriptionDto>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.PushSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new PushSubscriptionDto(s.Id, s.DeviceLabel, s.CreatedAtUtc, s.LastSuccessAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PushSubscriptionDto> RegisterAsync(
        RegisterPushSubscriptionDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.P256dh);
        ArgumentException.ThrowIfNullOrWhiteSpace(dto.Auth);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var endpointHash = Hash(dto.Endpoint);

        // The same endpoint can arrive again (the browser re-registered its own subscription,
        // or a shared device switched accounts): update the existing row in place rather than
        // violate the unique index on EndpointHash or leave a stale duplicate behind.
        var existing = await context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.EndpointHash == endpointHash, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = dto.Endpoint,
                EndpointHash = endpointHash,
                P256dh = dto.P256dh,
                Auth = dto.Auth,
                DeviceLabel = dto.DeviceLabel,
            };
            context.PushSubscriptions.Add(existing);
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = dto.P256dh;
            existing.Auth = dto.Auth;
            existing.DeviceLabel = dto.DeviceLabel;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PushSubscriptionDto(existing.Id, existing.DeviceLabel, existing.CreatedAtUtc, existing.LastSuccessAtUtc);
    }

    public async Task UnregisterAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var subscription = await context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
            return;

        context.PushSubscriptions.Remove(subscription);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var subscriptions = await context.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (subscriptions.Count == 0)
            return false;

        var payload = new PushNotificationPayload(
            "Brainy test notification",
            "Push notifications are working. You can turn them off any time in Account and data.",
            Category: null);

        var anySent = false;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var subscription in subscriptions)
        {
            var result = await sender.SendAsync(
                new PushSubscriptionEndpointDto(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                payload,
                cancellationToken).ConfigureAwait(false);

            switch (result.Outcome)
            {
                case PushSendOutcome.Sent:
                    subscription.LastSuccessAtUtc = now;
                    anySent = true;
                    break;
                case PushSendOutcome.Expired:
                    context.PushSubscriptions.Remove(subscription);
                    break;
                case PushSendOutcome.Failed:
                default:
                    break;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return anySent;
    }

    private static string Hash(string endpoint) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}
