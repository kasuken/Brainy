using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Brainy.Application.Telemetry;

/// <summary>
/// Single shared <see cref="ActivitySource"/>/<see cref="Meter"/> pair for Brainy's own custom
/// traces and metrics (issue #322). Both the Application and Web layers create spans/records
/// against this same logical source name so that <c>Brainy.Web</c>'s OpenTelemetry wiring only
/// has to enable one name (<see cref="Name"/>) to pick up everything Brainy emits, in addition
/// to the ASP.NET Core / EF Core / HttpClient / Blazor circuit instrumentation it configures
/// separately. See <c>Brainy.Web.Telemetry.TelemetryServiceCollectionExtensions</c> for how this
/// is registered, and <c>docs/production-runbook.md</c> for the exporter setup.
/// </summary>
/// <remarks>
/// <para>
/// <b>Privacy discipline (binding for every instrument added here):</b> no note content, note
/// or search titles, search terms, email addresses, free-form user text, or billing/AI secrets
/// may ever become a span attribute, a log attribute, or (especially) a metric tag. Metric tags
/// in particular must always come from a small, fixed, developer-controlled set of values (an
/// enum, a constant, a boolean) — never from anything a user typed — both to protect privacy
/// and because an unbounded/user-controlled tag value creates unbounded metric cardinality.
/// This mirrors the discipline already established by <c>SearchService</c>/<c>AnalyticsEvents</c>:
/// record that something happened and its fixed-vocabulary outcome, never the content involved.
/// </para>
/// </remarks>
public static class BrainyTelemetry
{
    /// <summary>
    /// The shared ActivitySource/Meter name. Registered by the Web layer via
    /// <c>TracerProviderBuilder.AddSource</c> / <c>MeterProviderBuilder.AddMeter</c> when
    /// telemetry is enabled; otherwise nothing is listening and these calls are near-zero-cost
    /// no-ops (see <see cref="System.Diagnostics.Activity.Current"/>/<see cref="ActivitySource.HasListeners"/>).
    /// </summary>
    public const string Name = "Brainy";

    /// <summary>Shared activity source for custom Brainy spans.</summary>
    public static ActivitySource ActivitySource { get; } = new(Name);

    /// <summary>Shared meter for custom Brainy metrics.</summary>
    public static Meter Meter { get; } = new(Name);

    // ── Cache (Application.Caching.MemoryApplicationCache; issue #322 / 5.13.2 cache) ────────

    /// <summary>
    /// Counts every <see cref="Interfaces.Caching.IApplicationCache.GetOrCreateAsync{T}"/> lookup,
    /// tagged with <c>cache.result</c> = <c>"hit"</c> or <c>"miss"</c>. The ratio of these two
    /// tag values over time is the cache hit ratio called for by the spec.
    /// </summary>
    public static Counter<long> CacheLookups { get; } = Meter.CreateCounter<long>(
        "brainy.cache.lookups",
        unit: "{lookup}",
        description: "Application cache lookups, tagged by hit/miss outcome.");

    /// <summary>Fixed tag values for <see cref="CacheLookups"/>' <c>cache.result</c> tag.</summary>
    public static class CacheResult
    {
        public const string Hit = "hit";
        public const string Miss = "miss";
    }

    // ── Offline capture sync (Application.Services.OfflineCaptureSyncService) ────────────────

    /// <summary>
    /// Records the size of each incoming offline-capture sync batch — the practical "queue
    /// depth" for this flow, since the queue itself lives client-side (IndexedDB) and this is
    /// the point where it is drained.
    /// </summary>
    public static Histogram<int> SyncBatchSize { get; } = Meter.CreateHistogram<int>(
        "brainy.sync.batch_size",
        unit: "{item}",
        description: "Number of items in each offline-capture sync batch submitted for processing.");

    /// <summary>
    /// Counts every synced item, tagged with <c>sync.outcome</c> (one of <see cref="SyncOutcome"/>).
    /// <see cref="SyncOutcome.Rejected"/> is the failure case called for by the spec.
    /// </summary>
    public static Counter<long> SyncItemsProcessed { get; } = Meter.CreateCounter<long>(
        "brainy.sync.items",
        unit: "{item}",
        description: "Offline-capture sync items processed, tagged by outcome.");

    /// <summary>Fixed tag values for <see cref="SyncItemsProcessed"/>' <c>sync.outcome</c> tag.</summary>
    public static class SyncOutcome
    {
        public const string Created = "created";
        public const string DuplicateIgnored = "duplicate_ignored";
        public const string AlreadySynced = "already_synced";
        public const string Rejected = "rejected";
    }

    // ── Billing webhooks (Application.Services.BillingWebhookProcessor) ──────────────────────

    /// <summary>
    /// Counts every processed billing webhook delivery, tagged with <c>billing.webhook_outcome</c>
    /// using the same fixed <c>Reason</c> vocabulary as <see cref="Brainy.Application.DTOs.Billing.BillingWebhookProcessingResult"/>
    /// (never the raw provider payload).
    /// </summary>
    public static Counter<long> BillingWebhookEvents { get; } = Meter.CreateCounter<long>(
        "brainy.billing.webhook_events",
        unit: "{event}",
        description: "Inbound billing webhook deliveries, tagged by outcome.");

    // ── Markdown/Obsidian vault export background job (Web.BackgroundServices) ───────────────

    /// <summary>Counts completed Markdown export background jobs, tagged with <c>export.outcome</c>.</summary>
    public static Counter<long> MarkdownExportJobs { get; } = Meter.CreateCounter<long>(
        "brainy.markdown_export.jobs",
        unit: "{job}",
        description: "Markdown/Obsidian vault export background jobs, tagged by outcome.");

    /// <summary>Records how long each Markdown export background job took to run.</summary>
    public static Histogram<double> MarkdownExportJobDuration { get; } = Meter.CreateHistogram<double>(
        "brainy.markdown_export.job.duration",
        unit: "ms",
        description: "Duration of Markdown/Obsidian vault export background jobs.");

    /// <summary>Fixed tag values for <see cref="MarkdownExportJobs"/>' <c>export.outcome</c> tag.</summary>
    public static class MarkdownExportOutcome
    {
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }
}
