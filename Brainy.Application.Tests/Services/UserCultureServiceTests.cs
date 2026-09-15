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

public sealed class UserCultureServiceTests
{
    private const string UserId = "culture-user";

    [Fact]
    public void AddBrainyApplication_RegistersUserCultureServiceAsTransient()
    {
        var services = new ServiceCollection();

        services.AddBrainyApplication();

        var registration = services.Single(service => service.ServiceType == typeof(IUserCultureService));
        registration.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    private static (IUserCultureService Sut, BrainyDbContext Db) BuildService(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserId));
        services.AddSingleton(TimeProvider.System);
        services.AddBrainyApplication();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IUserCultureService>(), provider.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task GetCultureIdAsync_WithNoPreferenceRecord_ReturnsNull()
    {
        var (sut, _) = BuildService(nameof(GetCultureIdAsync_WithNoPreferenceRecord_ReturnsNull));

        (await sut.GetCultureIdAsync()).Should().BeNull();
    }

    [Fact]
    public async Task SetCultureIdAsync_PersistsSupportedCulture()
    {
        var (sut, db) = BuildService(nameof(SetCultureIdAsync_PersistsSupportedCulture));

        await sut.SetCultureIdAsync("it-IT");

        (await sut.GetCultureIdAsync()).Should().Be("it-IT");
        (await db.DashboardPreferences.SingleAsync()).CultureId.Should().Be("it-IT");
    }

    [Fact]
    public async Task SetCultureIdAsync_IsCaseInsensitiveAgainstSupportedList()
    {
        var (sut, _) = BuildService(nameof(SetCultureIdAsync_IsCaseInsensitiveAgainstSupportedList));

        await sut.SetCultureIdAsync("IT-it");

        (await sut.GetCultureIdAsync()).Should().Be("IT-it");
    }

    [Fact]
    public async Task SetCultureIdAsync_WithUnsupportedCulture_RejectsWithoutPersisting()
    {
        var (sut, db) = BuildService(nameof(SetCultureIdAsync_WithUnsupportedCulture_RejectsWithoutPersisting));

        var act = () => sut.SetCultureIdAsync("fr-FR");

        await act.Should().ThrowAsync<ArgumentException>();
        (await db.DashboardPreferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetCultureIdAsync_CalledTwice_UpdatesExistingPreferenceRecord()
    {
        var (sut, db) = BuildService(nameof(SetCultureIdAsync_CalledTwice_UpdatesExistingPreferenceRecord));

        await sut.SetCultureIdAsync("it-IT");
        await sut.SetCultureIdAsync("en-US");

        (await sut.GetCultureIdAsync()).Should().Be("en-US");
        (await db.DashboardPreferences.CountAsync()).Should().Be(1);
    }
}
