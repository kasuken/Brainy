namespace Brainy.Application.DTOs.Offline;

/// <summary>
/// Read-only projection of the current-focus task for the Offline Lite (issue #302) Today
/// snapshot. Deliberately thin — just enough to render a static, non-interactive summary on
/// the offline fallback page.
/// </summary>
public sealed record OfflineCurrentFocusDto(Guid Id, string Title, string Status, DateTime? DueDate);

/// <summary>Read-only projection of one favorite/recent note for the Offline Lite Today snapshot.</summary>
public sealed record OfflineNoteSummaryDto(Guid Id, string Title, bool IsFavorite, DateTime UpdatedAtUtc);

/// <summary>
/// The full Offline Lite (issue #302) Today snapshot: current focus plus a short list of
/// favorited/recently-touched notes, scoped to the requesting user. Fetched by
/// <c>offlineSnapshot.js</c> while online and cached client-side (with
/// <see cref="GeneratedAtUtc"/> as the staleness anchor) for display on the static offline
/// fallback page — never rendered live, since it is a snapshot, not current data.
/// </summary>
public sealed record OfflineSnapshotDto(
    OfflineCurrentFocusDto? CurrentFocus,
    IReadOnlyList<OfflineNoteSummaryDto> Notes,
    DateTime GeneratedAtUtc);
