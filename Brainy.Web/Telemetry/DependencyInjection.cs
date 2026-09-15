using Brainy.Application.Telemetry;
using Brainy.Web.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Brainy.Web.Telemetry;

/// <summary>
/// Wires OpenTelemetry traces, metrics and logs with OTLP export (issue #322). Registers
/// nothing at all unless <see cref="TelemetryOptions.Enabled"/> is turned on — self-hosting is
/// unaffected by default, and every custom Activity/Meter call in <c>BrainyTelemetry</c> and
/// elsewhere becomes a near-zero-cost no-op when no provider is listening.
/// </summary>
public static class DependencyInjection
{
    // Blazor Server's own built-in ActivitySource/Meter names (.NET 8+; see "ASP.NET Core
    // Blazor performance best practices" > Metrics and tracing). Registering these — together
    // with AspNetCoreTraceInstrumentationOptions.EnableRazorComponentsSupport /
    // EnableAspNetCoreSignalRSupport below — is what correlates a request through the Blazor
    // circuit (UI events, JS interop callbacks, render batches) end to end, without any
    // hand-rolled CircuitHandler.
    private const string BlazorComponentsSource = "Microsoft.AspNetCore.Components";
    private const string BlazorCircuitsSource = "Microsoft.AspNetCore.Components.Server.Circuits";
    private const string BlazorLifecycleMeter = "Microsoft.AspNetCore.Components.Lifecycle";

    /// <summary>
    /// Binds and validates <see cref="TelemetryOptions"/>, then — only when
    /// <see cref="TelemetryOptions.Enabled"/> is <c>true</c> — registers OpenTelemetry tracing,
    /// metrics and logging: ASP.NET Core, EF Core and HttpClient instrumentation, Brainy's own
    /// custom traces/metrics (<see cref="BrainyTelemetry"/>), Blazor circuit correlation, and an
    /// OTLP exporter for all three signals. See <c>docs/production-runbook.md</c> for the
    /// collector-side setup this assumes.
    /// </summary>
    public static IServiceCollection AddBrainyTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Enabled || Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _),
                "Telemetry:OtlpEndpoint must be an absolute URL when Telemetry:Enabled is true.")
            .ValidateOnStart();

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        // Disabled by default: no OpenTelemetry provider is registered in DI at all, so
        // self-hosting sees zero added behavior/overhead and TracerProvider/MeterProvider
        // simply are not resolvable — read synchronously here, exactly like AddBilling and
        // AddAiAssistant already do for the same reason (the choice of *which services get
        // registered* has to be made once, at startup, not re-evaluated per request).
        if (!options.Enabled)
            return services;

        var protocol = string.Equals(options.Protocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        void ConfigureOtlpExporter(OtlpExporterOptions otlp)
        {
            otlp.Endpoint = new Uri(options.OtlpEndpoint!);
            otlp.Protocol = protocol;
            if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
                otlp.Headers = options.OtlpHeaders;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing => tracing
                // Applied as ParentBased: a trace that already started upstream (or in a
                // deeper span within this same process) is always honored regardless of ratio.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)))
                .AddSource(BrainyTelemetry.Name)
                .AddSource(BlazorComponentsSource)
                .AddSource(BlazorCircuitsSource)
                .AddAspNetCoreInstrumentation(aspnetCore =>
                {
                    // The platform's own liveness/readiness probes hit these continuously —
                    // noise, not signal, and never carry anything worth tracing.
                    aspnetCore.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                    aspnetCore.EnableRazorComponentsSupport = true;
                    aspnetCore.EnableAspNetCoreSignalRSupport = true;
                })
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                // Defense in depth: guarantees no SQL/command text, query string, or auth
                // header ever reaches the exporter, regardless of what any instrumentation
                // library captures today or starts capturing in a future version. Registered
                // before the exporter so it always runs first.
                .AddProcessor(new PrivacyRedactionProcessor())
                .AddOtlpExporter(ConfigureOtlpExporter))
            .WithMetrics(metrics => metrics
                .AddMeter(BrainyTelemetry.Name)
                .AddMeter(BlazorComponentsSource)
                .AddMeter(BlazorLifecycleMeter)
                .AddMeter(BlazorCircuitsSource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(ConfigureOtlpExporter))
            .WithLogging(logging => logging.AddOtlpExporter(ConfigureOtlpExporter));

        return services;
    }
}
