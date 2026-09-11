using Brainy.Application.DTOs.Capture;
using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IOfflineCaptureSyncService"/> on top of
/// <see cref="IShareCaptureService"/> plus an <see cref="OfflineCaptureSyncRecord"/>
/// idempotency ledger. Each item is checked/applied/recorded independently: a database-level
/// unique constraint on <see cref="OfflineCaptureSyncRecord.IdempotencyKey"/> is the primary
/// "exactly once" guarantee (the common "client retried after losing the response" case),
/// while <see cref="IShareCaptureService"/>'s own same-user/same-content duplicate window
/// remains a secondary safety net for the rare case where a Note was created but recording
/// the idempotency key was interrupted (e.g. a process crash between the two saves).
/// </summary>
internal sealed class OfflineCaptureSyncService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IShareCaptureService shareCaptureService,
    TimeProvider timeProvider) : IOfflineCaptureSyncService
{
    public async Task<OfflineCaptureSyncResultDto> SyncAsync(
        OfflineCaptureSyncBatchDto batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batch.Items);

        if (batch.Items.Count > IOfflineCaptureSyncService.MaxBatchSize)
            throw new ArgumentException(
                $"A sync batch cannot contain more than {IOfflineCaptureSyncService.MaxBatchSize} items.", nameof(batch));

        if (batch.Items.Count == 0)
            return new OfflineCaptureSyncResultDto([]);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<OfflineCaptureSyncItemResultDto>(batch.Items.Count);
        foreach (var item in batch.Items)
        {
            results.Add(await SyncOneAsync(userId, item, cancellationToken).ConfigureAwait(false));
        }

        return new OfflineCaptureSyncResultDto(results);
    }

    private async Task<OfflineCaptureSyncItemResultDto> SyncOneAsync(
        string userId, OfflineCaptureSyncItemDto item, CancellationToken cancellationToken)
    {
        // Scoped to the current user even though IdempotencyKey is globally unique: a stray
        // key collision (astronomically unlikely for a crypto-random GUID) must never let one
        // user's sync report success for another user's already-synced record.
        var existing = await context.OfflineCaptureSyncRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.IdempotencyKey == item.IdempotencyKey)
            .Select(r => (Guid?)r.NoteId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } existingNoteId)
        {
            return new OfflineCaptureSyncItemResultDto(
                item.IdempotencyKey, OfflineCaptureSyncOutcome.AlreadySynced, existingNoteId, null);
        }

        ShareCaptureResultDto captureResult;
        try
        {
            captureResult = await shareCaptureService
                .CaptureAsync(new ShareCaptureDto(item.Title, item.Text, item.Url), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new OfflineCaptureSyncItemResultDto(item.IdempotencyKey, OfflineCaptureSyncOutcome.Rejected, null, ex.Message);
        }

        context.OfflineCaptureSyncRecords.Add(new OfflineCaptureSyncRecord
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = item.IdempotencyKey,
            UserId = userId,
            NoteId = captureResult.Note.Id,
            SyncedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique index on IdempotencyKey: a concurrent retry of this exact item (e.g. the
            // foreground flush and a background-sync event firing at the same time) won the
            // race. The capture is safely stored either way, so report the win as success too.
            return new OfflineCaptureSyncItemResultDto(item.IdempotencyKey, OfflineCaptureSyncOutcome.AlreadySynced, captureResult.Note.Id, null);
        }

        var outcome = captureResult.Outcome == ShareCaptureOutcome.DuplicateIgnored
            ? OfflineCaptureSyncOutcome.DuplicateIgnored
            : OfflineCaptureSyncOutcome.Created;
        return new OfflineCaptureSyncItemResultDto(item.IdempotencyKey, outcome, captureResult.Note.Id, null);
    }
}
