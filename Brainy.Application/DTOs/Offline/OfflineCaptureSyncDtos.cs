namespace Brainy.Application.DTOs.Offline;

/// <summary>
/// One queued capture item submitted from the client-side Offline Lite (issue #302)
/// IndexedDB queue. <see cref="IdempotencyKey"/> is generated client-side (crypto-random)
/// when the capture is first queued, so retrying this exact item — after a dropped
/// connection, or a foreground-flush/background-sync race — still creates at most one Note.
/// </summary>
public sealed record OfflineCaptureSyncItemDto(Guid IdempotencyKey, string? Title, string? Text, string? Url);

/// <summary>A batch of queued captures submitted together to <c>POST /api/offline/captures/sync</c>.</summary>
public sealed record OfflineCaptureSyncBatchDto(IReadOnlyList<OfflineCaptureSyncItemDto> Items);

/// <summary>How one queued item's sync attempt resolved.</summary>
public enum OfflineCaptureSyncOutcome
{
    /// <summary>A new Inbox note was created for this item.</summary>
    Created,

    /// <summary>
    /// <see cref="Brainy.Application.Interfaces.Services.IShareCaptureService"/>'s own
    /// recent-duplicate window matched an existing note (e.g. the same content was also
    /// captured through another route moments earlier).
    /// </summary>
    DuplicateIgnored,

    /// <summary>
    /// This exact idempotency key was already synced by an earlier attempt (the common
    /// "retry after losing the response" case) — reported as a success, not re-applied.
    /// </summary>
    AlreadySynced,

    /// <summary>
    /// The server rejected this item (e.g. nothing capturable, or a length limit exceeded).
    /// This is a real validation failure, distinct from "still offline" — the client marks
    /// the item <c>failed</c> rather than leaving it <c>queued</c> for endless retry.
    /// </summary>
    Rejected
}

/// <summary>Result of one queued item's sync attempt.</summary>
public sealed record OfflineCaptureSyncItemResultDto(
    Guid IdempotencyKey,
    OfflineCaptureSyncOutcome Outcome,
    Guid? NoteId,
    string? Error);

/// <summary>Result of a full batch sync request, one entry per submitted item, same order.</summary>
public sealed record OfflineCaptureSyncResultDto(IReadOnlyList<OfflineCaptureSyncItemResultDto> Items);
