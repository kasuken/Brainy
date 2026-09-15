using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// One browser/device's Web Push (RFC 8030/8291) subscription for a user, keyed per
/// device — a user may have several (desktop, phone, a second browser) at once.
/// </summary>
/// <remarks>
/// <see cref="P256dh"/> and <see cref="Auth"/> are the subscription's own public encryption
/// keys, handed to us by the browser's push service — they authenticate and encrypt
/// deliveries to this one endpoint and carry no account secret. <see cref="Endpoint"/> can
/// exceed SQL Server's 900-byte unique-index key limit, so uniqueness is enforced instead via
/// <see cref="EndpointHash"/> (a SHA-256 hash), the same pattern <see cref="CalendarFeedToken"/>
/// uses for its token hash.
/// </remarks>
public class PushSubscription : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>The push service URL the browser gave us for this device.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 hash of <see cref="Endpoint"/>, used for the unique index.</summary>
    public string EndpointHash { get; set; } = string.Empty;

    /// <summary>Base64url-encoded P-256 Diffie-Hellman public key from the subscription's keys.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Base64url-encoded authentication secret from the subscription's keys.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>
    /// Optional, display-only browser/OS label (e.g. "Chrome on Windows") captured from
    /// <c>navigator.userAgent</c> at subscribe time, so the settings page can tell devices apart.
    /// Never used for any security decision.
    /// </summary>
    public string? DeviceLabel { get; set; }

    /// <summary>Set after the most recent push to this subscription that the push service accepted.</summary>
    public DateTime? LastSuccessAtUtc { get; set; }
}
