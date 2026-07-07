using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IArchiveRetentionService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// </summary>
public class ArchiveRetentionServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IArchiveRetentionService sut, BrainyDbContext db) BuildService(
        string dbName,
        string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o =>
            o.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IArchiveRetentionService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static ArchiveRetentionRule CreateRule(string userId, string entityType, int? retentionDays)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EntityType = entityType,
            RetentionDays = retentionDays
        };

    // ── GetRulesAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRulesAsync_WithNoRules_ReturnsEmpty()
    {
        var (sut, _) = BuildService(nameof(GetRulesAsync_WithNoRules_ReturnsEmpty));

        var result = await sut.GetRulesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRulesAsync_ReturnsOnlyCurrentUserRules()
    {
        var (sut, db) = BuildService(nameof(GetRulesAsync_ReturnsOnlyCurrentUserRules));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 30));
        db.ArchiveRetentionRules.Add(CreateRule(OtherUserId, "Note", 60));
        await db.SaveChangesAsync();

        var result = await sut.GetRulesAsync();

        result.Should().ContainSingle()
            .Which.RetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task GetRulesAsync_OrdersRulesByEntityType()
    {
        var (sut, db) = BuildService(nameof(GetRulesAsync_OrdersRulesByEntityType));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Task", 30));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 30));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Project", 30));
        await db.SaveChangesAsync();

        var result = await sut.GetRulesAsync();

        result.Select(r => r.EntityType).Should()
            .ContainInOrder("Note", "Project", "Task");
    }

    // ── UpsertRuleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertRuleAsync_WhenRuleDoesNotExist_CreatesIt()
    {
        var (sut, db) = BuildService(nameof(UpsertRuleAsync_WhenRuleDoesNotExist_CreatesIt));

        await sut.UpsertRuleAsync("Note", 90);

        var rule = await db.ArchiveRetentionRules.AsNoTracking().SingleAsync();
        rule.EntityType.Should().Be("Note");
        rule.RetentionDays.Should().Be(90);
        rule.UserId.Should().Be(DefaultUserId);
    }

    [Fact]
    public async Task UpsertRuleAsync_WhenRuleExists_UpdatesRetentionDays()
    {
        var (sut, db) = BuildService(nameof(UpsertRuleAsync_WhenRuleExists_UpdatesRetentionDays));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 30));
        await db.SaveChangesAsync();

        await sut.UpsertRuleAsync("Note", 180);

        var rule = await db.ArchiveRetentionRules.AsNoTracking().SingleAsync();
        rule.RetentionDays.Should().Be(180);
    }

    [Fact]
    public async Task UpsertRuleAsync_WithNullRetentionDays_PersistsIndefiniteRetention()
    {
        var (sut, db) = BuildService(nameof(UpsertRuleAsync_WithNullRetentionDays_PersistsIndefiniteRetention));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 30));
        await db.SaveChangesAsync();

        await sut.UpsertRuleAsync("Note", null);

        var rule = await db.ArchiveRetentionRules.AsNoTracking().SingleAsync();
        rule.RetentionDays.Should().BeNull();
    }

    [Fact]
    public async Task UpsertRuleAsync_WithWhitespaceEntityType_ThrowsArgumentException()
    {
        var (sut, _) = BuildService(nameof(UpsertRuleAsync_WithWhitespaceEntityType_ThrowsArgumentException));

        var act = () => sut.UpsertRuleAsync("   ", 30);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── DeleteRuleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRuleAsync_WhenRuleExists_RemovesIt()
    {
        var (sut, db) = BuildService(nameof(DeleteRuleAsync_WhenRuleExists_RemovesIt));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 30));
        await db.SaveChangesAsync();

        await sut.DeleteRuleAsync("Note");

        (await db.ArchiveRetentionRules.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteRuleAsync_WhenRuleDoesNotExist_DoesNothing()
    {
        var (sut, _) = BuildService(nameof(DeleteRuleAsync_WhenRuleDoesNotExist_DoesNothing));

        var act = () => sut.DeleteRuleAsync("Note");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteRuleAsync_DoesNotRemoveOtherUsersRule()
    {
        var (sut, db) = BuildService(nameof(DeleteRuleAsync_DoesNotRemoveOtherUsersRule));
        db.ArchiveRetentionRules.Add(CreateRule(OtherUserId, "Note", 30));
        await db.SaveChangesAsync();

        await sut.DeleteRuleAsync("Note");

        (await db.ArchiveRetentionRules.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // ── EnforceRetentionAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task EnforceRetentionAsync_WithoutSystemIdentity_ReturnsZeroAndDeletesNothing()
    {
        // Pins the documented contract: enforcement is a no-op until a system-level
        // (non user-scoped) identity provider is wired up. Nothing may be deleted.
        var (sut, db) = BuildService(nameof(EnforceRetentionAsync_WithoutSystemIdentity_ReturnsZeroAndDeletesNothing));
        db.ArchiveRetentionRules.Add(CreateRule(DefaultUserId, "Note", 0));
        db.Notes.Add(new Note
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Title = "Archived long ago",
            IsArchived = true,
            ArchivedAtUtc = DateTime.UtcNow.AddYears(-1)
        });
        await db.SaveChangesAsync();

        var deleted = await sut.EnforceRetentionAsync();

        deleted.Should().Be(0);
        (await db.Notes.AsNoTracking().CountAsync()).Should().Be(1);
    }
}
