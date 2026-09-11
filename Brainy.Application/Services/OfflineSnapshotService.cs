using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IOfflineSnapshotService"/> by reusing the same per-user-scoped reads
/// as the live Today page (<see cref="ITaskService.GetCurrentTaskAsync"/> and
/// <see cref="INoteService.GetAllAsync"/>) instead of querying the database directly, so this
/// snapshot can never drift from what those services consider "current focus" or "favorite".
/// </summary>
internal sealed class OfflineSnapshotService(
    ITaskService taskService,
    INoteService noteService,
    TimeProvider timeProvider) : IOfflineSnapshotService
{
    public async Task<OfflineSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var currentTask = await taskService.GetCurrentTaskAsync(cancellationToken).ConfigureAwait(false);
        var allNotes = await noteService.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var notes = allNotes
            .Where(n => !n.IsArchived)
            .OrderByDescending(n => n.IsFavorite)
            .ThenByDescending(n => n.UpdatedAtUtc)
            .Take(IOfflineSnapshotService.MaxNotes)
            .Select(n => new OfflineNoteSummaryDto(n.Id, n.Title, n.IsFavorite, n.UpdatedAtUtc))
            .ToList();

        var focus = currentTask is null
            ? null
            : new OfflineCurrentFocusDto(currentTask.Id, currentTask.Title, currentTask.Status.ToString(), currentTask.DueDate);

        return new OfflineSnapshotDto(focus, notes, timeProvider.GetUtcNow().UtcDateTime);
    }
}
