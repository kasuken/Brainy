using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Idempotency ledger for offline-captured content synced through the Offline Lite
/// (issue #302) client-side queue: records that a client-generated
/// <see cref="IdempotencyKey"/> has already produced a Note, so a batch retried after a
/// dropped connection or a duplicate background-sync/foreground-flush race creates each
/// queued capture at most once ("syncs exactly once" acceptance criterion). Mirrors
/// <see cref="ProcessedWebhookEvent"/>'s idempotency-ledger shape.
/// </summary>
public class OfflineCaptureSyncRecord : BaseEntity
{
    /// <summary>The client-generated (crypto-random) key assigned when the capture was queued offline.</summary>
    public Guid IdempotencyKey { get; set; }

    /// <summary>Identity key of the user the capture belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The Note created (or matched as a recent duplicate) for this capture.</summary>
    public Guid NoteId { get; set; }

    /// <summary>When this capture was synced to the server.</summary>
    public DateTime SyncedAtUtc { get; set; }
}
