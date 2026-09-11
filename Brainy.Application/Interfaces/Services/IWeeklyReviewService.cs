using Brainy.Application.DTOs.WeeklyReview;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Composes the guided weekly review (issue #299): the existing Week planning overview
/// plus Inbox age bands and previously-useful material resurfaced for a fresh look, each
/// with a small set of inline decisions (keep, link, archive, turn into task, dismiss).
/// "Schedule" is intentionally not duplicated here: for task-shaped work it is already the
/// existing Week page's "Add to week" / "Carry forward" actions (see <see cref="IWeekService"/>).
/// </summary>
public interface IWeeklyReviewService
{
    /// <summary>Loads the authenticated user's current guided weekly review.</summary>
    Task<WeeklyReviewDto> GetCurrentReviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// "Keep": dismisses the current review's prompt for this note without suppressing it
    /// from a future review — it may resurface again later based on its own recency signal.
    /// Records the decision for analytics only; no persistent state is written.
    /// </summary>
    Task KeepResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Dismiss": permanently suppresses this note from future resurfacing suggestions for
    /// the current user. Idempotent — dismissing an already-dismissed note is a no-op.
    /// </summary>
    Task DismissResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Link": links a resurfaced note into an active project owned by the current user,
    /// delegating to <c>INoteService.LinkToProjectAsync</c>.
    /// </summary>
    Task LinkResurfacedNoteAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Archive": archives a resurfaced note with a reason, delegating to
    /// <c>INoteService.ArchiveAsync</c>. Archived notes are excluded from future resurfacing.
    /// </summary>
    Task ArchiveResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Turn into task": distills a resurfaced note into an action and promotes it to a task
    /// in the given active project, delegating to <c>IActionItemService</c>. Reuses an
    /// existing action item on the note when one is already linked to a task.
    /// </summary>
    Task TurnResurfacedNoteIntoTaskAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default);
}
