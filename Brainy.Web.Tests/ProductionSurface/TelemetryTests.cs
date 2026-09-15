using AwesomeAssertions;
using Brainy.Web.Configuration;
using Brainy.Web.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Brainy.Web.Tests.ProductionSurface;

/// <summary>
/// Issue #322: OpenTelemetry must be off by default (so self-hosting is unaffected), and must
/// only turn on — with an OTLP endpoint required — when explicitly configured. Exercises
/// <c>AddBrainyTelemetry</c> directly against a plain <see cref="ServiceCollection"/>, the same
/// way <c>Brainy.Web.Tests.Identity.EmailConfirmationFlowTests</c> unit-tests <c>AddEmail</c> —
/// a real ASP.NET Core host (<c>WebApplicationFactory</c>) applies its own test configuration
/// only once <c>Program.cs</c> has already finished registering services, too late to affect a
/// provider-selection decision like this one that (like <c>AddBilling</c>/<c>AddAiAssistant</c>)
/// must run once at startup.
/// </summary>
public sealed class TelemetryTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrainyTelemetry(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Telemetry_IsDisabled_WhenNoConfigurationIsProvided()
    {
        // No Telemetry:* keys at all, matching a default appsettings.json for self-hosting.
        using var provider = BuildProvider([]);

        provider.GetService<TracerProvider>().Should().BeNull(
            "telemetry must be off by default so self-hosting is unaffected");
        provider.GetService<MeterProvider>().Should().BeNull(
            "telemetry must be off by default so self-hosting is unaffected");
    }

    [Fact]
    public void Telemetry_IsDisabled_WhenExplicitlyTurnedOff()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "false",
        });

        provider.GetService<TracerProvider>().Should().BeNull();
        provider.GetService<MeterProvider>().Should().BeNull();
    }

    [Fact]
    public void Telemetry_IsEnabled_WhenExplicitlyConfiguredWithAnOtlpEndpoint()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true",
            // Loopback endpoint that need not actually accept a connection: the OTLP exporter
            // batches and retries in the background, so nothing here blocks on reachability.
            ["Telemetry:OtlpEndpoint"] = "http://127.0.0.1:4317",
        });

        provider.GetRequiredService<TracerProvider>().Should().NotBeNull(
            "Telemetry:Enabled=true with a configured Telemetry:OtlpEndpoint must register tracing");
        provider.GetRequiredService<MeterProvider>().Should().NotBeNull(
            "Telemetry:Enabled=true with a configured Telemetry:OtlpEndpoint must register metrics");
    }

    [Fact]
    public void Telemetry_FailsValidation_WhenEnabledWithoutAnOtlpEndpoint()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true",
            // Deliberately omitted: Telemetry:OtlpEndpoint.
        });

        // Options validation (ValidateOnStart in production; enforced on first access here)
        // must reject this combination rather than silently exporting nowhere.
        var act = () => provider.GetRequiredService<IOptions<TelemetryOptions>>().Value;
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Telemetry:OtlpEndpoint*");
    }
}
