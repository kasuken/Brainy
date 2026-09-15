using System.Globalization;
using AwesomeAssertions;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Localization;

/// <summary>
/// AGENTS.md's correctness crux for this issue: "Dates such as due dates are user-calendar
/// dates; audit timestamps remain UTC" must hold no matter what UI culture is active. Due
/// dates and audit timestamps flow through the app as typed <see cref="DateTime"/> values —
/// never round-tripped through a culture-formatted string — so switching
/// <see cref="CultureInfo.CurrentCulture"/> must not change either boundary. These tests run
/// the exact same scenarios under it-IT that <c>UserTimeZoneServiceTests</c> already covers
/// under the ambient (invariant) culture, and assert identical results.
/// </summary>
public sealed class DueDateCultureBoundaryTests
{
    private const string UserId = "culture-boundary-user";
    private static readonly CultureInfo Italian = new("it-IT");

    private static (IUserTimeZoneService TimeZone, IUserCultureService Culture, BrainyDbContext Db) BuildServices(
        string databaseName,
        DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddBrainyApplication();
        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IUserTimeZoneService>(),
            provider.GetRequiredService<IUserCultureService>(),
            provider.GetRequiredService<BrainyDbContext>());
    }

    /// <summary>Runs <paramref name="action"/> with CurrentCulture/CurrentUICulture temporarily set, always restoring them.</summary>
    private static async Task RunUnderCultureAsync(CultureInfo culture, Func<Task> action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task GetUtcRangeAsync_UnderItalianCulture_ProducesSameUtcRangeAsUnderInvariant()
    {
        var (invariantSut, _, _) = BuildServices(
            nameof(GetUtcRangeAsync_UnderItalianCulture_ProducesSameUtcRangeAsUnderInvariant) + "-invariant",
            DateTimeOffset.UtcNow);
        await invariantSut.SetTimeZoneIdAsync("Europe/Zurich");
        var invariantRange = await invariantSut.GetUtcRangeAsync(new DateTime(2026, 3, 29), new DateTime(2026, 3, 29));

        var (italianSut, _, _) = BuildServices(
            nameof(GetUtcRangeAsync_UnderItalianCulture_ProducesSameUtcRangeAsUnderInvariant) + "-italian",
            DateTimeOffset.UtcNow);
        await italianSut.SetTimeZoneIdAsync("Europe/Zurich");

        (DateTime Start, DateTime End) italianRange = default;
        await RunUnderCultureAsync(Italian, async () =>
        {
            italianRange = await italianSut.GetUtcRangeAsync(new DateTime(2026, 3, 29), new DateTime(2026, 3, 29));
        });

        italianRange.Start.Should().Be(invariantRange.StartUtc);
        italianRange.End.Should().Be(invariantRange.EndUtc);
        // Sanity: this date range crosses the DST transition (23 hours), so the boundary is
        // actually exercised, not trivially equal because both sides are 24 hours apart.
        (italianRange.End - italianRange.Start).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task GetUserTodayAsync_UnderItalianCulture_UsesPersistedIanaTimeZoneAcrossUtcMidnightBoundary()
    {
        var (sut, _, _) = BuildServices(
            nameof(GetUserTodayAsync_UnderItalianCulture_UsesPersistedIanaTimeZoneAcrossUtcMidnightBoundary),
            new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero));
        await sut.SetTimeZoneIdAsync("Europe/Zurich");

        var today = DateTime.MinValue;
        await RunUnderCultureAsync(Italian, async () => today = await sut.GetUserTodayAsync());

        today.Should().Be(new DateTime(2026, 1, 2));
    }

    [Fact]
    public async Task SavingAPreference_UnderItalianCulture_StampsExactUtcAuditTimestamp()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var (_, cultureSut, db) = BuildServices(
            nameof(SavingAPreference_UnderItalianCulture_StampsExactUtcAuditTimestamp),
            fixedNow);

        await RunUnderCultureAsync(Italian, () => cultureSut.SetCultureIdAsync("it-IT"));

        var preference = await db.DashboardPreferences.SingleAsync();
        // Audit timestamps are plain DateTime assignments from TimeProvider, never a
        // culture-formatted string round-trip — must be the exact UTC instant regardless of
        // which culture was active while EF Core stamped it.
        preference.CreatedAtUtc.Should().Be(fixedNow.UtcDateTime);
        preference.UpdatedAtUtc.Should().Be(fixedNow.UtcDateTime);
    }
}
