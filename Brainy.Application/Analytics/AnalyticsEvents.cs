namespace Brainy.Application.Analytics;

/// <summary>
/// Fixed catalogue of internal product-analytics event names. This is the "public-facing
/// internal data dictionary" required by issue #295: every event Brainy may record is
/// declared here with its purpose, so <c>IAnalyticsService.TrackAsync</c> only ever accepts
/// a name from this list, never free text from a caller.
/// </summary>
/// <remarks>
/// Retention: every event recorded through this catalogue is kept for 24 months from
/// <c>OccurredAtUtc</c>, then eligible for deletion, unless the user deletes their account
/// or requests deletion sooner (see <c>AccountDeletionService</c>). All events are optional:
/// a user can opt out at any time via the analytics-consent toggle in Account &amp; data,
/// which stops all future writes for that user.
/// </remarks>
public static class AnalyticsEvents
{
    /// <summary>Retention period documented for every event in this catalogue.</summary>
    public const string DefaultRetentionPeriod = "24 months";

    // ── Activation ──────────────────────────────────────────────────────────

    /// <summary>The user captured their first note (fires once per user).</summary>
    public const string FirstCaptureCreated = "activation.first_capture";

    /// <summary>The user processed their first Inbox item out of the Inbox (fires once per user).</summary>
    public const string FirstInboxItemProcessed = "activation.first_processed";

    /// <summary>The user created their first task (fires once per user).</summary>
    public const string FirstTaskCreated = "activation.first_task";

    /// <summary>The user selected their first current-focus task (fires once per user).</summary>
    public const string FirstCurrentFocusSelected = "activation.first_focus";

    // ── Capture lifecycle ───────────────────────────────────────────────────

    /// <summary>A note was captured (every capture, not just the first).</summary>
    public const string CaptureCreated = "capture.created";

    /// <summary>An Inbox item was processed out of the Inbox.</summary>
    public const string InboxItemProcessed = "capture.processed";

    /// <summary>A captured note was linked into a project (reused as project input).</summary>
    public const string CaptureReusedAsProject = "capture.reused_project";

    /// <summary>A captured note's action item was promoted into a task (reused as task input).</summary>
    public const string CaptureReusedAsTask = "capture.reused_task";

    /// <summary>A captured note was used as source material for an Output (reused as output input).</summary>
    public const string CaptureReusedAsOutput = "capture.reused_output";

    // ── Tasks and focus ─────────────────────────────────────────────────────

    /// <summary>A task was created.</summary>
    public const string TaskCreated = "task.created";

    /// <summary>A task was set as the current-focus task.</summary>
    public const string CurrentFocusSelected = "focus.selected";

    /// <summary>
    /// A user added or edited a restart/handoff note from the Resume Context panel
    /// (issue #305). <c>PropertiesJson</c> carries no properties: only that a save happened,
    /// never the note's text or the task's title/description.
    /// </summary>
    public const string ResumeContextRestartNoteSaved = "focus.resume_context_restart_note_saved";

    // ── Search / retrieval ──────────────────────────────────────────────────

    /// <summary>A search query was submitted.</summary>
    public const string SearchSubmitted = "search.submitted";

    /// <summary>A search query returned zero results.</summary>
    public const string SearchZeroResult = "search.zero_result";

    /// <summary>A search result was opened.</summary>
    public const string SearchResultOpened = "search.result_opened";

    /// <summary>
    /// A follow-on action was taken on an opened search result (e.g. edited, linked,
    /// promoted) within the same session, used to gauge retrieval quality.
    /// </summary>
    public const string SearchFollowOnAction = "search.follow_on_action";

    // ── Weekly review ────────────────────────────────────────────────────────

    /// <summary>The user opened their current weekly review / week overview.</summary>
    public const string WeeklyReviewViewed = "review.weekly_viewed";

    /// <summary>The user carried an unfinished task forward from last week (resurfaced item).</summary>
    public const string ResurfacedItemActioned = "review.resurfaced_item_actioned";

    /// <summary>
    /// The user made a decision (keep, link, archive, turn into task, or dismiss) on a
    /// previously-useful note resurfaced by the guided weekly review (issue #299).
    /// <c>PropertiesJson</c> carries only the decision category, never note content.
    /// </summary>
    public const string ResurfacedNoteDecisionMade = "review.resurfaced_note_decision";

    // ── Onboarding journey (issue #294) ──────────────────────────────────────

    /// <summary>
    /// A step of the guided first-run onboarding journey was completed;
    /// <c>PropertiesJson</c> carries only the numeric step index, never note/task text.
    /// </summary>
    public const string OnboardingStepCompleted = "onboarding.step_completed";

    /// <summary>The user finished the entire onboarding journey (as opposed to skipping it).</summary>
    public const string OnboardingCompleted = "onboarding.completed";

    /// <summary>
    /// The user skipped/dismissed the onboarding journey; <c>PropertiesJson</c> carries only
    /// the step index they were on when they skipped.
    /// </summary>
    public const string OnboardingSkipped = "onboarding.skipped";

    // ── Feature engagement / retention ──────────────────────────────────────

    /// <summary>A named feature area was used; <c>PropertiesJson</c> carries the feature name only.</summary>
    public const string FeatureEngaged = "engagement.feature_used";

    // ── AI usage ─────────────────────────────────────────────────────────────

    /// <summary>An AI request was submitted (volume signal). Never carries the prompt or content sent.</summary>
    public const string AiRequestSubmitted = "ai.request_submitted";

    /// <summary>An AI request failed. Never carries the prompt, response content, or error detail beyond a category.</summary>
    public const string AiRequestFailed = "ai.request_failed";

    /// <summary>
    /// An AI-generated suggestion was reviewed by the user; <c>PropertiesJson</c> records
    /// only whether it was accepted as-is or edited, never the suggested or edited text.
    /// </summary>
    public const string AiSuggestionReviewed = "ai.suggestion_reviewed";

    /// <summary>
    /// A user's plan tier changed (issue #296). Fired by <c>IEntitlementService.SetPlanTierAsync</c>,
    /// whether the change came from the internal/admin path or a billing webhook.
    /// <c>PropertiesJson</c> carries only the new tier name.
    /// </summary>
    public const string PlanConverted = "monetization.plan_converted";

    /// <summary>
    /// A user hit a plan limit (active-project cap, AI unavailable on their tier, or AI
    /// allowance exhausted) and was blocked. <c>PropertiesJson</c> carries only which limit
    /// and which plan tier, never note/task/AI content.
    /// </summary>
    public const string PlanLimitReached = "monetization.plan_limit_reached";

    /// <summary>All declared event names, used to render the data dictionary page.</summary>
    public static readonly IReadOnlyList<AnalyticsEventDescriptor> All =
    [
        new(FirstCaptureCreated, "Detect activation: time-to-first-capture.", DefaultRetentionPeriod, true),
        new(FirstInboxItemProcessed, "Detect activation: time-to-first-processed-item.", DefaultRetentionPeriod, true),
        new(FirstTaskCreated, "Detect activation: time-to-first-task.", DefaultRetentionPeriod, true),
        new(FirstCurrentFocusSelected, "Detect activation: time-to-first-focus-selection.", DefaultRetentionPeriod, true),
        new(CaptureCreated, "Measure capture volume over time.", DefaultRetentionPeriod, true),
        new(InboxItemProcessed, "Measure Inbox processing throughput.", DefaultRetentionPeriod, true),
        new(CaptureReusedAsProject, "Measure capture-to-project reuse (retrieval value).", DefaultRetentionPeriod, true),
        new(CaptureReusedAsTask, "Measure capture-to-task reuse (retrieval value).", DefaultRetentionPeriod, true),
        new(CaptureReusedAsOutput, "Measure capture-to-output reuse (retrieval value).", DefaultRetentionPeriod, true),
        new(TaskCreated, "Measure task-creation volume.", DefaultRetentionPeriod, true),
        new(CurrentFocusSelected, "Measure current-focus usage.", DefaultRetentionPeriod, true),
        new(ResumeContextRestartNoteSaved, "Measure use of restart notes on the Resume Context panel, without collecting task content.", DefaultRetentionPeriod, true),
        new(SearchSubmitted, "Measure search usage volume.", DefaultRetentionPeriod, true),
        new(SearchZeroResult, "Measure search-result coverage gaps.", DefaultRetentionPeriod, true),
        new(SearchResultOpened, "Measure search result click-through (retrieval success).", DefaultRetentionPeriod, true),
        new(SearchFollowOnAction, "Measure whether an opened result led to further action (retrieval quality).", DefaultRetentionPeriod, true),
        new(WeeklyReviewViewed, "Measure weekly-review engagement.", DefaultRetentionPeriod, true),
        new(ResurfacedItemActioned, "Measure whether resurfaced/carried-forward items get acted on.", DefaultRetentionPeriod, true),
        new(ResurfacedNoteDecisionMade, "Measure which decision users take on resurfaced notes in the guided weekly review.", DefaultRetentionPeriod, true),
        new(OnboardingStepCompleted, "Measure onboarding funnel step-by-step completion.", DefaultRetentionPeriod, true),
        new(OnboardingCompleted, "Measure full onboarding-journey completion rate.", DefaultRetentionPeriod, true),
        new(OnboardingSkipped, "Measure where users skip out of the onboarding journey.", DefaultRetentionPeriod, true),
        new(FeatureEngaged, "Measure per-feature engagement for D7/D30 retention analysis.", DefaultRetentionPeriod, true),
        new(AiRequestSubmitted, "Measure AI request volume.", DefaultRetentionPeriod, true),
        new(AiRequestFailed, "Measure AI request failure rate.", DefaultRetentionPeriod, true),
        new(AiSuggestionReviewed, "Measure AI suggestion acceptance/edit rate.", DefaultRetentionPeriod, true),
        new(PlanConverted, "Measure paid-plan conversion.", DefaultRetentionPeriod, true),
        new(PlanLimitReached, "Measure which plan limits users hit, to inform pricing and limit decisions.", DefaultRetentionPeriod, true),
    ];
}

/// <summary>One row of the public-facing internal data dictionary.</summary>
public sealed record AnalyticsEventDescriptor(
    string EventName,
    string Purpose,
    string RetentionPeriod,
    bool IsOptional);
