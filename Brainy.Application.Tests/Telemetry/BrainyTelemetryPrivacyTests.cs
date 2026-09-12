using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Brainy.Application.DTOs.Billing;
using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Billing;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Telemetry;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Telemetry;

/// <summary>
/// Issue #322's hard privacy constraint, exercised against the actual instruments: every
/// metric Brainy emits for caching, offline sync, and billing webhooks must carry only a
/// small, fixed, developer-controlled tag vocabulary — never a search term, note/capture
/// text, URL, user id, or exception message. Captures real measurements via a
/// <see cref="MeterListener"/> subscribed to <see cref="BrainyTelemetry.Meter"/>, the same way
/// an OpenTelemetry SDK metric reader would, rather than asserting against the source code.
/// </summary>
public sealed class BrainyTelemetryPrivacyTests
{
    private const string SensitiveNoteText = "my therapist appointment notes — do not share";
    private const string SensitiveUrl = "https://example.com/secret-diary-entry";

    private sealed record Measurement(string InstrumentName, object? Value, IReadOnlyDictionary<string, object?> Tags);

    private static (List<Measurement> Measurements, MeterListener Listener) Listen()
    {
        var measurements = new List<Measurement>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == BrainyTelemetry.Name)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, ToDictionary(tags))));
        listener.Start();
        return (measurements, listener);
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
            dictionary[tag.Key] = tag.Value;
        return dictionary;
    }

    /// <summary>Every tag value recorded, across every instrument, from every signal Brainy emits.</summary>
    private static IEnumerable<object?> AllTagValues(List<Measurement> measurements) =>
        measurements.SelectMany(m => m.Tags.Values);

    /// <summary>True when <paramref name="value"/> is a string containing <paramref name="text"/>.</summary>
    private static bool ContainsText(object? value, string text) => value is string s && s.Contains(text);

    [Fact]
    public async Task CacheLookups_NeverTagsAnythingOtherThanFixedHitMissVocabulary()
    {
        var services = new ServiceCollection();
        services.AddBrainyApplication();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IApplicationCache>();

        var (measurements, listener) = Listen();
        using var listenerLease = listener;

        // Miss, then hit — the cached value itself is a sensitive-looking string, to prove it
        // never leaks into a tag.
        await cache.GetOrCreateAsync("user-1", "notes:all", ["notes"],
            _ => Task.FromResult(SensitiveNoteText));
        await cache.GetOrCreateAsync("user-1", "notes:all", ["notes"],
            _ => Task.FromResult(SensitiveNoteText));

        // Other tests in this assembly may exercise the same shared static Meter concurrently
        // (xunit parallelizes across test classes by default), so assertions here check for
        // presence and for the fixed vocabulary invariant holding — never an exact count or
        // order, which a concurrently-running test could legitimately add to.
        var lookups = measurements.Where(m => m.InstrumentName == "brainy.cache.lookups").ToList();
        lookups.Should().NotBeEmpty();
        lookups.Should().OnlyContain(m =>
            Equals(m.Tags["cache.result"], BrainyTelemetry.CacheResult.Hit) ||
            Equals(m.Tags["cache.result"], BrainyTelemetry.CacheResult.Miss));
        lookups.Should().Contain(m => Equals(m.Tags["cache.result"], BrainyTelemetry.CacheResult.Miss));
        lookups.Should().Contain(m => Equals(m.Tags["cache.result"], BrainyTelemetry.CacheResult.Hit));

        AllTagValues(measurements).Should().NotContain(v => ContainsText(v, SensitiveNoteText));
    }

    [Fact]
    public async Task OfflineSync_TagsOnlyFixedOutcomesAndNeverCaptureContent()
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(nameof(OfflineSync_TagsOnlyFixedOutcomesAndNeverCaptureContent)));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService("sync-privacy-user"));
        services.AddBrainyApplication();
        await using var provider = services.BuildServiceProvider();
        var sync = provider.GetRequiredService<IOfflineCaptureSyncService>();

        var (measurements, listener) = Listen();
        using var listenerLease = listener;

        var batch = new OfflineCaptureSyncBatchDto([
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), "Title", SensitiveNoteText, SensitiveUrl), // Created
            new OfflineCaptureSyncItemDto(Guid.NewGuid(), null, null, null), // Rejected: nothing to capture
        ]);

        var result = await sync.SyncAsync(batch);
        result.Items[0].Outcome.Should().Be(OfflineCaptureSyncOutcome.Created);
        result.Items[1].Outcome.Should().Be(OfflineCaptureSyncOutcome.Rejected);
        result.Items[1].Error.Should().NotBeNullOrEmpty(); // sanity: the rejection message exists...

        var allowedSyncOutcomes = new[]
        {
            BrainyTelemetry.SyncOutcome.Created,
            BrainyTelemetry.SyncOutcome.DuplicateIgnored,
            BrainyTelemetry.SyncOutcome.AlreadySynced,
            BrainyTelemetry.SyncOutcome.Rejected,
        };
        var itemMeasurements = measurements.Where(m => m.InstrumentName == "brainy.sync.items").ToList();
        itemMeasurements.Should().NotBeEmpty();
        itemMeasurements.Should().OnlyContain(m => allowedSyncOutcomes.Contains(m.Tags["sync.outcome"]));
        itemMeasurements.Should().Contain(m => Equals(m.Tags["sync.outcome"], BrainyTelemetry.SyncOutcome.Created));
        itemMeasurements.Should().Contain(m => Equals(m.Tags["sync.outcome"], BrainyTelemetry.SyncOutcome.Rejected));

        var batchSizeMeasurements = measurements.Where(m => m.InstrumentName == "brainy.sync.batch_size").ToList();
        batchSizeMeasurements.Should().Contain(m => Equals(m.Value, 2));

        // ...but it never appears as, or inside, a tag value.
        AllTagValues(measurements).Should().NotContain(v =>
            ContainsText(v, SensitiveNoteText) || ContainsText(v, SensitiveUrl) || ContainsText(v, result.Items[1].Error!));
    }

    [Fact]
    public async Task BillingWebhookEvents_TagsOnlyTheFixedReasonVocabulary()
    {
        const string userId = "billing-privacy-user";
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(nameof(BillingWebhookEvents_TagsOnlyTheFixedReasonVocabulary)));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        var parsedEvent = new ParsedBillingWebhookEvent("evt_privacy_1", "subscription.updated", userId, PlanTier.Pro, null);
        services.AddSingleton<IBillingProvider>(new FakeBillingProvider(parsedEvent));
        services.AddBrainyApplication();
        await using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<IBillingWebhookProcessor>();

        var (measurements, listener) = Listen();
        using var listenerLease = listener;

        await processor.ProcessAsync("{}", "wrong-signature"); // InvalidSignature
        await processor.ProcessAsync("{\"id\":\"evt_privacy_1\"}", "valid-signature"); // Applied
        await processor.ProcessAsync("{\"id\":\"evt_privacy_1\"}", "valid-signature"); // AlreadyProcessed

        var allowedReasons = new[]
        {
            BillingWebhookProcessingResult.InvalidSignature.Reason,
            BillingWebhookProcessingResult.Ignored.Reason,
            BillingWebhookProcessingResult.AlreadyProcessed.Reason,
            BillingWebhookProcessingResult.Applied.Reason,
        };
        var events = measurements.Where(m => m.InstrumentName == "brainy.billing.webhook_events").ToList();
        events.Should().NotBeEmpty();
        events.Should().OnlyContain(m => allowedReasons.Contains(m.Tags["billing.webhook_outcome"]));
        events.Should().Contain(m => Equals(m.Tags["billing.webhook_outcome"], BillingWebhookProcessingResult.InvalidSignature.Reason));
        events.Should().Contain(m => Equals(m.Tags["billing.webhook_outcome"], BillingWebhookProcessingResult.Applied.Reason));
        events.Should().Contain(m => Equals(m.Tags["billing.webhook_outcome"], BillingWebhookProcessingResult.AlreadyProcessed.Reason));

        // Never the raw payload, the target user id, or any provider event id.
        AllTagValues(measurements).Should().NotContain(v => ContainsText(v, userId) || ContainsText(v, "evt_privacy_1"));
    }
}
