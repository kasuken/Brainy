using System.ComponentModel.DataAnnotations;

namespace Brainy.Web.Configuration;

/// <summary>
/// Configuration for OpenTelemetry traces, metrics and logs (issue #322). Disabled by default
/// so self-hosting is unaffected: no OpenTelemetry provider is registered at all unless
/// <see cref="Enabled"/> is explicitly turned on and <see cref="OtlpEndpoint"/> is configured.
/// See <c>docs/production-runbook.md</c> for the exporter setup this deployment uses.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>
    /// Master switch. When <c>false</c> (the default), <c>AddBrainyTelemetry</c> registers no
    /// OpenTelemetry providers/instrumentation and every custom Activity/Meter call in the app
    /// is a near-zero-cost no-op (nothing is listening).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OTLP collector endpoint (e.g. <c>https://otel-collector.internal:4317</c>). Required
    /// when <see cref="Enabled"/> is <c>true</c>. Shared by the trace, metric and log
    /// exporters — Brainy does not need per-signal endpoints.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Optional OTLP protocol, either <c>grpc</c> (default, port 4317) or <c>httpprotobuf</c>
    /// (port 4318). Must match what <see cref="OtlpEndpoint"/> is listening for.
    /// </summary>
    public string Protocol { get; set; } = "grpc";

    /// <summary>
    /// Optional extra OTLP export headers, e.g. an ingestion API key, in the same
    /// comma-separated <c>key1=value1,key2=value2</c> form the OpenTelemetry SDK's own
    /// <c>OTEL_EXPORTER_OTLP_HEADERS</c> environment variable uses. Configure this via an
    /// environment variable or a secret store, never a checked-in appsettings file — it is
    /// used only to authenticate to the collector and is never itself exported as telemetry.
    /// </summary>
    public string? OtlpHeaders { get; set; }

    /// <summary>The <c>service.name</c> resource attribute reported to the collector.</summary>
    [Required]
    public string ServiceName { get; set; } = "Brainy";

    /// <summary>
    /// Fraction of traces to sample, from 0.0 (none) to 1.0 (all). Applied as a
    /// <c>ParentBased(TraceIdRatioBased(...))</c> sampler, so a trace that already started
    /// upstream is always honored. Metrics and logs are unaffected by this setting — only
    /// trace volume (and therefore export cost) scales with it. Defaults to full sampling,
    /// which is appropriate for Brainy's traffic volume; lower it if request volume grows.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SamplingRatio { get; set; } = 1.0;
}
