using AwesomeAssertions;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Covers <see cref="ICalendarFeedTokenService"/>'s credential lifecycle: generation only
/// ever exposes the raw token once, revoking/regenerating take effect immediately, and only
/// a hash is ever persisted (see <see cref="Brainy.Domain.Entities.CalendarFeedToken"/>).
/// </summary>
public class CalendarFeedTokenServiceTests
{
    private const string UserA = "token-user-a";
    private const string UserB = "token-user-b";

    private static (ICalendarFeedTokenService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = UserA)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ICalendarFeedTokenService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task GetStatusAsync_WithNoToken_ReportsNoActiveToken()
    {
        var (sut, _) = BuildService(nameof(GetStatusAsync_WithNoToken_ReportsNoActiveToken));

        var status = await sut.GetStatusAsync();

        status.HasActiveToken.Should().BeFalse();
        status.CreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RegenerateAsync_StoresOnlyAHash_NeverTheRawToken()
    {
        var (sut, db) = BuildService(nameof(RegenerateAsync_StoresOnlyAHash_NeverTheRawToken));

        var result = await sut.RegenerateAsync();

        var stored = await db.CalendarFeedTokens.AsNoTracking().SingleAsync(t => t.UserId == UserA);
        stored.TokenHash.Should().NotBeNullOrEmpty();
        stored.TokenHash.Should().NotBe(result.RawToken, "only a hash of the token may ever be persisted");
        stored.TokenHash.Should().HaveLength(64, "a hex-encoded SHA-256 hash is 64 characters");
        result.RawToken.Should().HaveLength(64, "a hex-encoded 256-bit token is 64 characters");
    }

    [Fact]
    public async Task RegenerateAsync_CalledTwice_InvalidatesThePreviousRawToken()
    {
        var (sut, _) = BuildService(nameof(RegenerateAsync_CalledTwice_InvalidatesThePreviousRawToken));

        var first = await sut.RegenerateAsync();
        var second = await sut.RegenerateAsync();

        first.RawToken.Should().NotBe(second.RawToken);
        (await sut.ResolveUserIdAsync(first.RawToken)).Should().BeNull("regenerating must invalidate the previous link immediately");
        (await sut.ResolveUserIdAsync(second.RawToken)).Should().Be(UserA);
    }

    [Fact]
    public async Task ResolveUserIdAsync_WithValidToken_ReturnsOwningUser()
    {
        var (sut, _) = BuildService(nameof(ResolveUserIdAsync_WithValidToken_ReturnsOwningUser));

        var result = await sut.RegenerateAsync();
        var resolved = await sut.ResolveUserIdAsync(result.RawToken);

        resolved.Should().Be(UserA);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000000")] // 66 hex chars, wrong length
    public async Task ResolveUserIdAsync_WithGarbageInput_ReturnsNullWithoutThrowing(string? garbage)
    {
        var (sut, _) = BuildService(nameof(ResolveUserIdAsync_WithGarbageInput_ReturnsNullWithoutThrowing) + garbage);

        var resolved = await sut.ResolveUserIdAsync(garbage);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_BreaksResolutionImmediately()
    {
        var (sut, _) = BuildService(nameof(RevokeAsync_BreaksResolutionImmediately));
        var result = await sut.RegenerateAsync();

        await sut.RevokeAsync();

        (await sut.ResolveUserIdAsync(result.RawToken)).Should().BeNull();
        var status = await sut.GetStatusAsync();
        status.HasActiveToken.Should().BeFalse();
        status.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeAsync_WhenNoTokenExists_DoesNotThrow()
    {
        var (sut, _) = BuildService(nameof(RevokeAsync_WhenNoTokenExists_DoesNotThrow));

        var act = () => sut.RevokeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OneUsersToken_NeverResolvesToAnotherUser()
    {
        const string dbName = nameof(OneUsersToken_NeverResolvesToAnotherUser);
        var (sutA, _) = BuildService(dbName, UserA);
        var tokenA = await sutA.RegenerateAsync();

        var (sutB, _) = BuildService(dbName, UserB);
        var tokenB = await sutB.RegenerateAsync();

        (await sutA.ResolveUserIdAsync(tokenA.RawToken)).Should().Be(UserA);
        (await sutA.ResolveUserIdAsync(tokenB.RawToken)).Should().Be(UserB, "resolution depends only on the token, not on which user's service instance resolves it");

        // Revoking A's token must not touch B's.
        await sutA.RevokeAsync();
        (await sutA.ResolveUserIdAsync(tokenA.RawToken)).Should().BeNull();
        (await sutB.ResolveUserIdAsync(tokenB.RawToken)).Should().Be(UserB);
    }
}
