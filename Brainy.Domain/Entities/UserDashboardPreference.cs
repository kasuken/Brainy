using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Stores per-user layout preferences for the dashboard (widget order, collapsed state, thresholds).
/// One record per user; created on first access, updated on any preference change.
/// </summary>
public class UserDashboardPreference : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>JSON array of widget names in user-chosen order, e.g. ["CurrentTask","Overdue","DueToday","ThisWeek","NextTasks","HighPriority","InboxReminder","FocusSummary"]</summary>
    public string? WidgetOrder { get; set; }

    /// <summary>JSON array of widget names the user has collapsed.</summary>
    public string? CollapsedWidgets { get; set; }

    /// <summary>Inbox count threshold at which a warning is shown. Default 10.</summary>
    public int InboxWarningThreshold { get; set; } = 10;

    /// <summary>
    /// IANA time-zone id used to interpret the user's calendar day. UTC is the safe
    /// default until the browser reports a more specific zone.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
}
