using Brainy.Application.DTOs.Goals;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Entities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="IGoalMilestoneService"/> resolved via the real DI container
/// with an EF Core InMemory database. Each test uses a unique database name for isolation.
/// Milestones have no UserId of their own — every operation must verify ownership
/// through the parent goal.
/// </summary>
public class GoalMilestoneServiceTests
{
    private const string DefaultUserId = "u1";
    private const string OtherUserId = "u2";

    private static (IGoalMilestoneService sut, BrainyDbContext db) BuildService(
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
        return (sp.GetRequiredService<IGoalMilestoneService>(), sp.GetRequiredService<BrainyDbContext>());
    }

    private static Goal CreateGoal(string userId)
        => new() { Id = Guid.NewGuid(), UserId = userId, Title = "Goal" };

    private static GoalMilestone CreateMilestone(Guid goalId, string title = "Milestone")
        => new() { Id = Guid.NewGuid(), GoalId = goalId, Title = title };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithOwnedGoal_PersistsMilestone()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WithOwnedGoal_PersistsMilestone));
        var goal = CreateGoal(DefaultUserId);
        db.Goals.Add(goal);
        await db.SaveChangesAsync();

        var result = await sut.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Ship v1"));

        var stored = await db.GoalMilestones.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.GoalId.Should().Be(goal.Id);
        stored.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_WhenGoalBelongsToAnotherUser_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CreateAsync_WhenGoalBelongsToAnotherUser_ThrowsKeyNotFoundException));
        var foreignGoal = CreateGoal(OtherUserId);
        db.Goals.Add(foreignGoal);
        await db.SaveChangesAsync();

        var act = () => sut.CreateAsync(new CreateGoalMilestoneDto(foreignGoal.Id, "Ship v1"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByGoalAsync_ReturnsOnlyMilestonesOfOwnedGoal()
    {
        var (sut, db) = BuildService(nameof(GetByGoalAsync_ReturnsOnlyMilestonesOfOwnedGoal));
        var ownGoal = CreateGoal(DefaultUserId);
        var foreignGoal = CreateGoal(OtherUserId);
        db.Goals.AddRange(ownGoal, foreignGoal);
        db.GoalMilestones.Add(CreateMilestone(ownGoal.Id, "Mine"));
        db.GoalMilestones.Add(CreateMilestone(foreignGoal.Id, "Foreign"));
        await db.SaveChangesAsync();

        var result = await sut.GetByGoalAsync(ownGoal.Id);

        result.Should().ContainSingle()
            .Which.Title.Should().Be("Mine");
    }

    [Fact]
    public async Task GetByGoalAsync_WhenGoalBelongsToAnotherUser_ReturnsEmpty()
    {
        var (sut, db) = BuildService(nameof(GetByGoalAsync_WhenGoalBelongsToAnotherUser_ReturnsEmpty));
        var foreignGoal = CreateGoal(OtherUserId);
        db.Goals.Add(foreignGoal);
        db.GoalMilestones.Add(CreateMilestone(foreignGoal.Id));
        await db.SaveChangesAsync();

        var result = await sut.GetByGoalAsync(foreignGoal.Id);

        result.Should().BeEmpty();
    }

    // ── Complete / Uncomplete ─────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SetsCompletedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_SetsCompletedFlagAndTimestamp));
        var goal = CreateGoal(DefaultUserId);
        var milestone = CreateMilestone(goal.Id);
        db.Goals.Add(goal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        await sut.CompleteAsync(milestone.Id);

        var stored = await db.GoalMilestones.AsNoTracking().SingleAsync();
        stored.IsCompleted.Should().BeTrue();
        stored.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task UncompleteAsync_ClearsCompletedFlagAndTimestamp()
    {
        var (sut, db) = BuildService(nameof(UncompleteAsync_ClearsCompletedFlagAndTimestamp));
        var goal = CreateGoal(DefaultUserId);
        var milestone = CreateMilestone(goal.Id);
        milestone.IsCompleted = true;
        milestone.CompletedAtUtc = DateTime.UtcNow;
        db.Goals.Add(goal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        await sut.UncompleteAsync(milestone.Id);

        var stored = await db.GoalMilestones.AsNoTracking().SingleAsync();
        stored.IsCompleted.Should().BeFalse();
        stored.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_WhenMilestoneBelongsToAnotherUsersGoal_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(CompleteAsync_WhenMilestoneBelongsToAnotherUsersGoal_ThrowsKeyNotFoundException));
        var foreignGoal = CreateGoal(OtherUserId);
        var milestone = CreateMilestone(foreignGoal.Id);
        db.Goals.Add(foreignGoal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        var act = () => sut.CompleteAsync(milestone.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenMilestoneExists_RemovesIt()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenMilestoneExists_RemovesIt));
        var goal = CreateGoal(DefaultUserId);
        var milestone = CreateMilestone(goal.Id);
        db.Goals.Add(goal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        await sut.DeleteAsync(milestone.Id);

        (await db.GoalMilestones.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WhenMilestoneBelongsToAnotherUsersGoal_ThrowsKeyNotFoundException()
    {
        var (sut, db) = BuildService(nameof(DeleteAsync_WhenMilestoneBelongsToAnotherUsersGoal_ThrowsKeyNotFoundException));
        var foreignGoal = CreateGoal(OtherUserId);
        var milestone = CreateMilestone(foreignGoal.Id);
        db.Goals.Add(foreignGoal);
        db.GoalMilestones.Add(milestone);
        await db.SaveChangesAsync();

        var act = () => sut.DeleteAsync(milestone.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
