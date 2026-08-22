using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

public sealed class UserTimeZoneServiceTests
{
    private const string UserId = "timezone-user";

    [Fact]
    public void AddBrainyApplication_RegistersUserTimeZoneServiceAsTransient()
    {
        var services = new ServiceCollection();

        services.AddBrainyApplication();

        var registration = services.Single(service => service.ServiceType == typeof(IUserTimeZoneService));
        registration.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    private static (IUserTimeZoneService Sut, BrainyDbContext Db) BuildService(
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
        return (provider.GetRequiredService<IUserTimeZoneService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task GetUserTodayAsync_UsesPersistedIanaTimeZoneAcrossUtcMidnightBoundary()
    {
        var (sut, _) = BuildService(
            nameof(GetUserTodayAsync_UsesPersistedIanaTimeZoneAcrossUtcMidnightBoundary),
            new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero));
        await sut.SetTimeZoneIdAsync("Europe/Zurich");

        var today = await sut.GetUserTodayAsync();

        today.Should().Be(new DateTime(2026, 1, 2));
    }

    [Fact]
    public void Model_RequiresOneDashboardPreferencePerUser()
    {
        var (_, db) = BuildService(
            nameof(Model_RequiresOneDashboardPreferencePerUser),
            DateTimeOffset.UtcNow);

        var preferenceType = db.Model.FindEntityType(typeof(Brainy.Domain.Entities.UserDashboardPreference));
        var userIdIndex = preferenceType!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(["UserId"]));

        userIdIndex.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task SetTimeZoneIdAsync_WithUnknownId_RejectsWithoutPersistingPreference()
    {
        var (sut, db) = BuildService(
            nameof(SetTimeZoneIdAsync_WithUnknownId_RejectsWithoutPersistingPreference),
            DateTimeOffset.UtcNow);

        var act = () => sut.SetTimeZoneIdAsync("Mars/Olympus_Mons");

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.DashboardPreferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetUtcRangeAsync_RespectsDaylightSavingTransition()
    {
        var (sut, _) = BuildService(
            nameof(GetUtcRangeAsync_RespectsDaylightSavingTransition),
            DateTimeOffset.UtcNow);
        await sut.SetTimeZoneIdAsync("Europe/Zurich");

        var (start, end) = await sut.GetUtcRangeAsync(
            new DateTime(2026, 3, 29), new DateTime(2026, 3, 29));

        (end - start).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public async Task SetTimeZoneOverrideAsync_PersistsExplicitOverride()
    {
        var (sut, db) = BuildService(
            nameof(SetTimeZoneOverrideAsync_PersistsExplicitOverride),
            DateTimeOffset.UtcNow);

        await sut.SetTimeZoneOverrideAsync("Europe/Zurich");

        (await sut.GetTimeZoneOverrideIdAsync()).Should().Be("Europe/Zurich");
        (await sut.GetTimeZoneIdAsync()).Should().Be("Europe/Zurich");
        (await db.DashboardPreferences.SingleAsync()).TimeZoneId.Should().Be("manual:Europe/Zurich");
    }

    [Fact]
    public async Task SetTimeZoneIdAsync_AfterOverride_ClearsOverrideMarker()
    {
        var (sut, db) = BuildService(
            nameof(SetTimeZoneIdAsync_AfterOverride_ClearsOverrideMarker),
            DateTimeOffset.UtcNow);
        await sut.SetTimeZoneOverrideAsync("Europe/Zurich");

        await sut.SetTimeZoneIdAsync("UTC");

        (await sut.GetTimeZoneOverrideIdAsync()).Should().BeNull();
        (await sut.GetTimeZoneIdAsync()).Should().Be("UTC");
        (await db.DashboardPreferences.SingleAsync()).TimeZoneId.Should().Be("UTC");
    }
}
