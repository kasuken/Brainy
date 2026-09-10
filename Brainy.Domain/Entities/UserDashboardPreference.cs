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

    /// <summary>
    /// Consent flag for internal product-analytics event tracking (see
    /// <c>Brainy.Application.Analytics.AnalyticsEvents</c>). Defaults to opted-in with clear
    /// disclosure and a one-click opt-out; toggling this off makes
    /// <c>IAnalyticsService.TrackAsync</c> a no-op for this user going forward.
    /// </summary>
    public bool AnalyticsEnabled { get; set; } = true;

    /// <summary>
    /// Starter Mode trims <c>NavMenu</c> to Today, Inbox, Projects and Search, hiding
    /// advanced planning/analytics/AI surfaces behind a "Show full navigation" expander.
    /// Defaults to on for new accounts (see issue #294) and can be toggled independently
    /// of onboarding progress at any time from Account &amp; data.
    /// </summary>
    public bool StarterModeEnabled { get; set; } = true;

    /// <summary>
    /// True once the user has finished (not merely skipped) the first-run onboarding
    /// journey: capture, process, next action, current focus. Stops the onboarding
    /// entry point from being surfaced again.
    /// </summary>
    public bool OnboardingCompleted { get; set; }

    /// <summary>
    /// True once the user has explicitly dismissed/skipped the onboarding journey outright
    /// (as opposed to completing it). Kept distinct from <see cref="OnboardingCompleted"/>
    /// so product analytics can tell "finished" apart from "opted out".
    /// </summary>
    public bool OnboardingDismissed { get; set; }

    /// <summary>
    /// Index of the onboarding step the user last reached, so the guided journey can be
    /// resumed exactly where it was left off after a skip or a session break.
    /// </summary>
    public int OnboardingStep { get; set; }
}
