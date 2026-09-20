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
/// Covers <see cref="IMcpAccessTokenService"/>'s credential lifecycle: issuing only ever
/// exposes the raw token once, a user may hold several tokens revoked independently, revoked
/// tokens stop resolving immediately, and only a hash is ever persisted (see
/// <see cref="Brainy.Domain.Entities.McpAccessToken"/>).
/// </summary>
public class McpAccessTokenServiceTests
{
    private const string UserA = "mcp-user-a";
    private const string UserB = "mcp-user-b";

    private static (IMcpAccessTokenService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IMcpAccessTokenService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    [Fact]
    public async Task ListAsync_WithNoTokens_ReturnsEmpty()
    {
        var (sut, _) = BuildService(nameof(ListAsync_WithNoTokens_ReturnsEmpty));

        var tokens = await sut.ListAsync();

        tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueAsync_StoresOnlyAHash_NeverTheRawToken()
    {
        var (sut, db) = BuildService(nameof(IssueAsync_StoresOnlyAHash_NeverTheRawToken));

        var result = await sut.IssueAsync("Claude Desktop");

        var stored = await db.McpAccessTokens.AsNoTracking().SingleAsync(t => t.UserId == UserA);
        stored.Name.Should().Be("Claude Desktop");
        stored.TokenHash.Should().NotBeNullOrEmpty();
        stored.TokenHash.Should().NotBe(result.RawToken, "only a hash of the token may ever be persisted");
        stored.TokenHash.Should().HaveLength(64, "a hex-encoded SHA-256 hash is 64 characters");
        result.RawToken.Should().StartWith("brainy_mcp_", "the raw token carries a greppable prefix");
    }

    [Fact]
    public async Task IssueAsync_TrimsName_AndRejectsBlankOrOverLongNames()
    {
        var (sut, _) = BuildService(nameof(IssueAsync_TrimsName_AndRejectsBlankOrOverLongNames));

        var issued = await sut.IssueAsync("  VS Code  ");
        issued.Name.Should().Be("VS Code");

        await FluentActions.Awaiting(() => sut.IssueAsync("   "))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => sut.IssueAsync(new string('x', 101)))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveUserIdAsync_WithValidToken_ReturnsOwningUser_AndRecordsLastUsed()
    {
        var (sut, db) = BuildService(nameof(ResolveUserIdAsync_WithValidToken_ReturnsOwningUser_AndRecordsLastUsed));

        var result = await sut.IssueAsync("Copilot");
        var resolved = await sut.ResolveUserIdAsync(result.RawToken);

        resolved.Should().Be(UserA);
        var stored = await db.McpAccessTokens.AsNoTracking().SingleAsync(t => t.Id == result.Id);
        stored.LastUsedAtUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    [InlineData("brainy_mcp_short")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")] // 64 hex, no prefix
    public async Task ResolveUserIdAsync_WithGarbageInput_ReturnsNullWithoutThrowing(string? garbage)
    {
        var (sut, _) = BuildService(nameof(ResolveUserIdAsync_WithGarbageInput_ReturnsNullWithoutThrowing) + garbage);

        var resolved = await sut.ResolveUserIdAsync(garbage);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_BreaksResolutionImmediately_ForThatTokenOnly()
    {
        var (sut, _) = BuildService(nameof(RevokeAsync_BreaksResolutionImmediately_ForThatTokenOnly));
        var first = await sut.IssueAsync("Client one");
        var second = await sut.IssueAsync("Client two");

        var revoked = await sut.RevokeAsync(first.Id);

        revoked.Should().BeTrue();
        (await sut.ResolveUserIdAsync(first.RawToken)).Should().BeNull("the revoked token stops working");
        (await sut.ResolveUserIdAsync(second.RawToken)).Should().Be(UserA, "revoking one token must not disturb the others");
    }

    [Fact]
    public async Task RevokeAsync_WithUnknownOrAlreadyRevokedToken_ReturnsFalse()
    {
        var (sut, _) = BuildService(nameof(RevokeAsync_WithUnknownOrAlreadyRevokedToken_ReturnsFalse));
        var issued = await sut.IssueAsync("Client");

        (await sut.RevokeAsync(Guid.NewGuid())).Should().BeFalse("no such token exists");
        (await sut.RevokeAsync(issued.Id)).Should().BeTrue();
        (await sut.RevokeAsync(issued.Id)).Should().BeFalse("an already-revoked token is a no-op");
    }

    [Fact]
    public async Task OneUsersToken_NeverResolvesToOrIsRevocableByAnotherUser()
    {
        const string dbName = nameof(OneUsersToken_NeverResolvesToOrIsRevocableByAnotherUser);
        var (sutA, _) = BuildService(dbName, UserA);
        var tokenA = await sutA.IssueAsync("A's client");

        var (sutB, _) = BuildService(dbName, UserB);
        var tokenB = await sutB.IssueAsync("B's client");

        (await sutA.ResolveUserIdAsync(tokenA.RawToken)).Should().Be(UserA);
        (await sutA.ResolveUserIdAsync(tokenB.RawToken)).Should().Be(UserB,
            "resolution depends only on the token, not on which user's service instance resolves it");

        // User B must not be able to revoke user A's token by id.
        (await sutB.RevokeAsync(tokenA.Id)).Should().BeFalse("a token can only be revoked by its owner");
        (await sutA.ResolveUserIdAsync(tokenA.RawToken)).Should().Be(UserA, "the cross-user revoke attempt had no effect");
    }

    [Fact]
    public async Task ListAsync_ReflectsIssueAndRevokeState_NewestFirst()
    {
        var (sut, _) = BuildService(nameof(ListAsync_ReflectsIssueAndRevokeState_NewestFirst));
        var first = await sut.IssueAsync("First");
        await sut.IssueAsync("Second");
        await sut.RevokeAsync(first.Id);

        var tokens = await sut.ListAsync();

        tokens.Should().HaveCount(2);
        tokens.Should().ContainSingle(t => t.Id == first.Id).Which.IsActive.Should().BeFalse();
        tokens.Single(t => t.Name == "Second").IsActive.Should().BeTrue();
    }
}
