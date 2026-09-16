namespace Brainy.Web.Components.Onboarding;

/// <summary>
/// Circuit-scoped bridge that lets any page ask for the guided onboarding journey to be
/// (re)opened, without that page needing a reference to <c>OnboardingJourney</c> itself.
/// </summary>
/// <remarks>
/// The journey lives in <c>MainLayout</c> so it can float above whatever page is open, but
/// the natural places to offer it again — Today's empty state, for example — are inside the
/// page, on the far side of the layout boundary. Before this existed, a user who dismissed
/// the journey could only get it back from Account &amp; data, which is exactly where someone
/// who is still finding their way around is least likely to look.
///
/// Mirrors the <c>ThemeService</c> pattern: scoped per circuit, raises an event, and the
/// single subscriber in the layout does the real work.
/// </remarks>
public sealed class OnboardingJourneyLauncher
{
    /// <summary>Raised when a page asks for the journey to be reopened from the start.</summary>
    public event Func<Task>? OnRestartRequested;

    /// <summary>
    /// Asks the mounted journey to reset its persisted progress and show itself again from
    /// step one. A no-op when nothing is subscribed (for example on a statically rendered
    /// page, where the journey is deliberately not mounted).
    /// </summary>
    public Task RequestRestartAsync() => OnRestartRequested?.Invoke() ?? Task.CompletedTask;
}
