using AwesomeAssertions;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Covers <see cref="IOutputShareLinkService"/>'s credential lifecycle for issue #319: the raw
/// token is exposed only once, revoking/regenerating take effect immediately, expiry is
/// honored, only a hash is ever persisted, the public resolution path never leaks anything
/// beyond the output's own content, and one user can never manage or read another's link.
/// </summary>
public class OutputShareLinkServiceTests
{
    private const string UserA = "share-user-a";
    private const string UserB = "share-user-b";

    private static (IOutputShareLinkService sut, BrainyDbContext db, FixedTimeProvider clock) BuildService(
        string dbName,
        string userId = UserA)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddSingleton<TimeProvider>(clock);
        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IOutputShareLinkService>(), sp.GetRequiredService<BrainyDbContext>(), clock);
    }

    private static Output NewOutput(string userId, string title = "My Output", string? description = "A description", string content = "# Hello\n\nBody text.") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = title,
        Description = description,
        Content = content,
        Type = OutputType.Report,
        Status = OutputStatus.Draft
    };

    [Fact]
    public async Task GetStatusAsync_WithNoLink_ReportsNoActiveLink()
    {
        var (sut, db, _) = BuildService(nameof(GetStatusAsync_WithNoLink_ReportsNoActiveLink));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        var status = await sut.GetStatusAsync(output.Id);

        status.Should().NotBeNull();
        status!.IsActive.Should().BeFalse();
        status.CreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ForAnOutputThatDoesNotExist_ReturnsNull()
    {
        var (sut, _, _) = BuildService(nameof(GetStatusAsync_ForAnOutputThatDoesNotExist_ReturnsNull));

        var status = await sut.GetStatusAsync(Guid.NewGuid());

        status.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ForAnotherUsersOutput_ReturnsNull()
    {
        const string dbName = nameof(GetStatusAsync_ForAnotherUsersOutput_ReturnsNull);
        var (sutA, db, _) = BuildService(dbName, UserA);
        var outputB = NewOutput(UserB);
        db.Outputs.Add(outputB);
        await db.SaveChangesAsync();

        var status = await sutA.GetStatusAsync(outputB.Id);

        status.Should().BeNull("an output's share status must never be readable by anyone but its owner");
    }

    [Fact]
    public async Task EnableAsync_StoresOnlyAHash_NeverTheRawToken()
    {
        var (sut, db, _) = BuildService(nameof(EnableAsync_StoresOnlyAHash_NeverTheRawToken));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        var result = await sut.EnableAsync(output.Id, expiresAtUtc: null);

        var stored = await db.OutputShareLinks.AsNoTracking().SingleAsync(l => l.OutputId == output.Id);
        stored.TokenHash.Should().NotBeNullOrEmpty();
        stored.TokenHash.Should().NotBe(result.RawToken, "only a hash of the token may ever be persisted");
        stored.TokenHash.Should().HaveLength(64, "a hex-encoded SHA-256 hash is 64 characters");
        result.RawToken.Should().HaveLength(64, "a hex-encoded 256-bit token is 64 characters");
    }

    [Fact]
    public async Task EnableAsync_ForAnOutputNotOwnedByTheCurrentUser_Throws()
    {
        const string dbName = nameof(EnableAsync_ForAnOutputNotOwnedByTheCurrentUser_Throws);
        var (sutA, db, _) = BuildService(dbName, UserA);
        var outputB = NewOutput(UserB);
        db.Outputs.Add(outputB);
        await db.SaveChangesAsync();

        var act = () => sutA.EnableAsync(outputB.Id, expiresAtUtc: null);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task EnableAsync_CalledTwice_InvalidatesThePreviousRawToken()
    {
        var (sut, db, _) = BuildService(nameof(EnableAsync_CalledTwice_InvalidatesThePreviousRawToken));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        var first = await sut.EnableAsync(output.Id, expiresAtUtc: null);
        var second = await sut.EnableAsync(output.Id, expiresAtUtc: null);

        first.RawToken.Should().NotBe(second.RawToken);
        (await sut.ResolvePublicAsync(first.RawToken)).Should().BeNull("regenerating must invalidate the previous link immediately");
        (await sut.ResolvePublicAsync(second.RawToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task EnableAsync_CalledTwice_LeavesExactlyOneRowForTheOutput()
    {
        var (sut, db, _) = BuildService(nameof(EnableAsync_CalledTwice_LeavesExactlyOneRowForTheOutput));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        await sut.EnableAsync(output.Id, expiresAtUtc: null);
        await sut.EnableAsync(output.Id, expiresAtUtc: null);

        (await db.OutputShareLinks.CountAsync(l => l.OutputId == output.Id)).Should().Be(1);
    }

    [Fact]
    public async Task EnableAsync_WithAPastExpiry_Throws()
    {
        var (sut, db, clock) = BuildService(nameof(EnableAsync_WithAPastExpiry_Throws));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        var act = () => sut.EnableAsync(output.Id, clock.GetUtcNow().UtcDateTime.AddDays(-1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolvePublicAsync_WithAValidToken_ReturnsOnlyTheOutputsOwnContent()
    {
        var (sut, db, _) = BuildService(nameof(ResolvePublicAsync_WithAValidToken_ReturnsOnlyTheOutputsOwnContent));
        var output = NewOutput(UserA, "Shareable title", "A public-safe description", "# Body\n\nSome content.");
        db.Outputs.Add(output);
        await db.SaveChangesAsync();
        var created = await sut.EnableAsync(output.Id, expiresAtUtc: null);

        var shared = await sut.ResolvePublicAsync(created.RawToken);

        shared.Should().NotBeNull();
        shared!.Title.Should().Be("Shareable title");
        shared.Description.Should().Be("A public-safe description");
        shared.Content.Should().Be("# Body\n\nSome content.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000000")] // 66 hex chars, wrong length
    public async Task ResolvePublicAsync_WithGarbageInput_ReturnsNullWithoutThrowing(string? garbage)
    {
        var (sut, _, _) = BuildService(nameof(ResolvePublicAsync_WithGarbageInput_ReturnsNullWithoutThrowing) + garbage);

        var resolved = await sut.ResolvePublicAsync(garbage);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolvePublicAsync_WithAnUnknownButPlausibleToken_ReturnsNull()
    {
        var (sut, _, _) = BuildService(nameof(ResolvePublicAsync_WithAnUnknownButPlausibleToken_ReturnsNull));

        var resolved = await sut.ResolvePublicAsync(new string('a', 64));

        resolved.Should().BeNull("a token that was never issued must resolve to nothing, exactly like a revoked one");
    }

    [Fact]
    public async Task RevokeAsync_BreaksResolutionImmediately()
    {
        var (sut, db, _) = BuildService(nameof(RevokeAsync_BreaksResolutionImmediately));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();
        var created = await sut.EnableAsync(output.Id, expiresAtUtc: null);

        await sut.RevokeAsync(output.Id);

        (await sut.ResolvePublicAsync(created.RawToken)).Should().BeNull();
        var status = await sut.GetStatusAsync(output.Id);
        status!.IsActive.Should().BeFalse();
        status.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeAsync_WhenNoLinkExists_DoesNotThrow()
    {
        var (sut, db, _) = BuildService(nameof(RevokeAsync_WhenNoLinkExists_DoesNotThrow));
        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();

        var act = () => sut.RevokeAsync(output.Id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAsync_ForAnotherUsersOutput_DoesNotRevokeIt()
    {
        const string dbName = nameof(RevokeAsync_ForAnotherUsersOutput_DoesNotRevokeIt);
        var (sutB, db, _) = BuildService(dbName, UserB);
        var outputA = NewOutput(UserA);
        db.Outputs.Add(outputA);
        await db.SaveChangesAsync();

        var (sutA, _, _) = BuildService(dbName, UserA);
        var created = await sutA.EnableAsync(outputA.Id, expiresAtUtc: null);

        // UserB does not own outputA, so revoking by its id from UserB's service must be a
        // no-op rather than reaching across ownership boundaries.
        await sutB.RevokeAsync(outputA.Id);

        (await sutA.ResolvePublicAsync(created.RawToken)).Should().NotBeNull("another user's revoke call must not affect an output they do not own");
    }

    [Fact]
    public async Task ResolvePublicAsync_AfterExpiry_ReturnsNull()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        var dbName = nameof(ResolvePublicAsync_AfterExpiry_ReturnsNull);
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(UserA));
        services.AddSingleton<TimeProvider>(clock);
        services.AddBrainyApplication();
        var sp = services.BuildServiceProvider();
        var sut = sp.GetRequiredService<IOutputShareLinkService>();
        var db = sp.GetRequiredService<BrainyDbContext>();

        var output = NewOutput(UserA);
        db.Outputs.Add(output);
        await db.SaveChangesAsync();
        var created = await sut.EnableAsync(output.Id, clock.GetUtcNow().UtcDateTime.AddHours(1));

        (await sut.ResolvePublicAsync(created.RawToken)).Should().NotBeNull("not yet expired");

        clock.Advance(TimeSpan.FromHours(2));

        (await sut.ResolvePublicAsync(created.RawToken)).Should().BeNull("an expired link must resolve to nothing, exactly like a revoked one");
    }

    [Fact]
    public async Task OneOutputsLink_NeverResolvesAnotherOutput()
    {
        const string dbName = nameof(OneOutputsLink_NeverResolvesAnotherOutput);
        var (sutA, db, _) = BuildService(dbName, UserA);
        var outputA = NewOutput(UserA, "A's output");
        db.Outputs.Add(outputA);
        await db.SaveChangesAsync();
        var tokenA = await sutA.EnableAsync(outputA.Id, expiresAtUtc: null);

        var (sutB, dbB, _) = BuildService(dbName, UserB);
        var outputB = NewOutput(UserB, "B's output");
        dbB.Outputs.Add(outputB);
        await dbB.SaveChangesAsync();
        var tokenB = await sutB.EnableAsync(outputB.Id, expiresAtUtc: null);

        (await sutA.ResolvePublicAsync(tokenA.RawToken))!.Title.Should().Be("A's output");
        (await sutA.ResolvePublicAsync(tokenB.RawToken))!.Title.Should().Be("B's output", "resolution depends only on the token, not on which user's service instance resolves it");

        // Revoking A's link must not touch B's.
        await sutA.RevokeAsync(outputA.Id);
        (await sutA.ResolvePublicAsync(tokenA.RawToken)).Should().BeNull();
        (await sutB.ResolvePublicAsync(tokenB.RawToken))!.Title.Should().Be("B's output");
    }
}
