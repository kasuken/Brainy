using Brainy.Application.DTOs.Dashboard;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Manages per-user dashboard layout and threshold preferences.
/// A preference record is created on first access if one does not yet exist.
/// </summary>
public interface IUserDashboardPreferenceService
{
    /// <summary>
    /// Returns the current user's dashboard preferences, creating a default record if none exists.
    /// </summary>
    Task<UserDashboardPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's dashboard preferences and returns the updated record.
    /// </summary>
    Task<UserDashboardPreferenceDto> UpdateAsync(UpdateDashboardPreferenceDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns Starter Mode (trimmed nav: Today, Inbox, Projects, Search) on or off for the
    /// current user. Independent of onboarding-journey progress — can be toggled at any time.
    /// </summary>
    Task<UserDashboardPreferenceDto> SetStarterModeAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the onboarding-journey step the current user last reached, so the guided
    /// journey resumes from that step next time it is shown instead of restarting.
    /// </summary>
    Task<UserDashboardPreferenceDto> SetOnboardingStepAsync(int step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the onboarding journey as completed (finished, not merely skipped) for the
    /// current user. The onboarding entry point stops being surfaced.
    /// </summary>
    Task<UserDashboardPreferenceDto> CompleteOnboardingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the onboarding journey as dismissed/skipped outright for the current user.
    /// Kept distinct from <see cref="CompleteOnboardingAsync"/> for funnel measurement.
    /// </summary>
    Task<UserDashboardPreferenceDto> DismissOnboardingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears completed/dismissed flags and resets the step index to 0, so the guided
    /// journey is shown again from the start (e.g. "Replay the guided tour" in Account &amp; data).
    /// </summary>
    Task<UserDashboardPreferenceDto> ResetOnboardingAsync(CancellationToken cancellationToken = default);
}
