using Brainy.Application.Common;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Goals;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Tests.Fakes;
using Brainy.Data;
using Brainy.Domain.Enums;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Brainy.Application.Tests.Services;

/// <summary>
/// Integration tests for <see cref="IGoalService"/> and <see cref="IGoalMilestoneService"/>
/// using the real DI container with an EF Core InMemory database.
/// Each test uses a unique database name for full isolation.
/// </summary>
public class GoalServiceTests
{
    private const string DefaultUserId = "goal-test-user-1";

    private static (IGoalService Goals, IGoalMilestoneService Milestones, IAreaService Areas)
        BuildServices(string dbName, string userId = DefaultUserId)
    {
        var services = new ServiceCollection();

        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));

        services.AddScoped<Brainy.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<BrainyDbContext>());

        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));

        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IGoalService>(),
            provider.GetRequiredService<IGoalMilestoneService>(),
            provider.GetRequiredService<IAreaService>()
        );
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutArea_ReturnsGoalWithNullAreaId()
    {
        var (goals, _, _) = BuildServices(nameof(CreateAsync_WithoutArea_ReturnsGoalWithNullAreaId));

        var result = await goals.CreateAsync(new CreateGoalDto("Run a marathon"));

        result.Id.Should().NotBeEmpty();
        result.Title.Should().Be("Run a marathon");
        result.AreaId.Should().BeNull();
        result.Status.Should().Be(GoalStatus.Planned);
    }

    [Fact]
    public async Task CreateAsync_WithValidArea_ReturnsGoalWithAreaId()
    {
        var (goals, _, areas) = BuildServices(nameof(CreateAsync_WithValidArea_ReturnsGoalWithAreaId));

        var area = await areas.CreateAsync(new CreateAreaDto("Health"));
        var result = await goals.CreateAsync(new CreateGoalDto("Run a marathon", area.Id));

        result.AreaId.Should().Be(area.Id);
        result.AreaName.Should().Be("Health");
    }

    [Fact]
    public async Task CreateAsync_WithNonExistentArea_ThrowsKeyNotFoundException()
    {
        var (goals, _, _) = BuildServices(nameof(CreateAsync_WithNonExistentArea_ThrowsKeyNotFoundException));

        var act = () => goals.CreateAsync(new CreateGoalDto("Goal", Guid.NewGuid()));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_WithAreaBelongingToAnotherUser_ThrowsKeyNotFoundException()
    {
        var dbName = nameof(CreateAsync_WithAreaBelongingToAnotherUser_ThrowsKeyNotFoundException);
        var (_, _, areasUser2) = BuildServices(dbName, "user-2");
        var (goalsUser1, _, _) = BuildServices(dbName, "user-1");

        var area = await areasUser2.CreateAsync(new CreateAreaDto("User 2 Area"));

        var act = () => goalsUser1.CreateAsync(new CreateGoalDto("Stolen goal", area.Id));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── GetAllNonArchivedAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetAllNonArchivedAsync_ExcludesArchivedGoals()
    {
        var (goals, _, _) = BuildServices(nameof(GetAllNonArchivedAsync_ExcludesArchivedGoals));

        var g1 = await goals.CreateAsync(new CreateGoalDto("Active goal"));
        var g2 = await goals.CreateAsync(new CreateGoalDto("To archive"));
        await goals.ArchiveAsync(g2.Id);

        var result = await goals.GetAllNonArchivedAsync();

        result.Should().ContainSingle(g => g.Id == g1.Id);
        result.Should().NotContain(g => g.Id == g2.Id);
    }

    [Fact]
    public async Task GetAllNonArchivedAsync_DoesNotReturnOtherUsersGoals()
    {
        var dbName = nameof(GetAllNonArchivedAsync_DoesNotReturnOtherUsersGoals);
        var (goalsUser1, _, _) = BuildServices(dbName, "user-1");
        var (goalsUser2, _, _) = BuildServices(dbName, "user-2");

        await goalsUser2.CreateAsync(new CreateGoalDto("User 2 goal"));
        var result = await goalsUser1.GetAllNonArchivedAsync();

        result.Should().BeEmpty();
    }

    // ── UpdateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesTitle_ReturnsDtoWithNewTitle()
    {
        var (goals, _, _) = BuildServices(nameof(UpdateAsync_ChangesTitle_ReturnsDtoWithNewTitle));

        var goal = await goals.CreateAsync(new CreateGoalDto("Old title"));
        var updated = await goals.UpdateAsync(new UpdateGoalDto(goal.Id, "New title", null, null, null, GoalStatus.Active));

        updated.Title.Should().Be("New title");
        updated.Status.Should().Be(GoalStatus.Active);
    }

    [Fact]
    public async Task UpdateAsync_SetStatusToAchieved_SetsAchievedDate()
    {
        var (goals, _, _) = BuildServices(nameof(UpdateAsync_SetStatusToAchieved_SetsAchievedDate));

        var goal = await goals.CreateAsync(new CreateGoalDto("Finish a book"));
        var updated = await goals.UpdateAsync(new UpdateGoalDto(goal.Id, goal.Title, null, null, null, GoalStatus.Achieved));

        updated.Status.Should().Be(GoalStatus.Achieved);
        updated.AchievedDate.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (goals, _, _) = BuildServices(nameof(UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var goal = await goals.CreateAsync(new CreateGoalDto("Run a marathon"));

        var act = () => goals.UpdateAsync(new UpdateGoalDto(
            goal.Id, goal.Title, goal.AreaId, goal.Description, goal.TargetDate, goal.Status,
            RowVersion: [1, 2, 3]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── ArchiveAsync / RestoreAsync ──────────────────────────────────────────

    [Fact]
    public async Task ArchiveAsync_SetsIsArchivedAndStatusArchived()
    {
        var (goals, _, _) = BuildServices(nameof(ArchiveAsync_SetsIsArchivedAndStatusArchived));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal to archive"));
        await goals.ArchiveAsync(goal.Id);

        var archived = await goals.GetByIdAsync(goal.Id);

        archived.Should().NotBeNull();
        archived!.IsArchived.Should().BeTrue();
        archived.Status.Should().Be(GoalStatus.Archived);
        archived.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ClearsIsArchivedAndResetsStatusToPlanned()
    {
        var (goals, _, _) = BuildServices(nameof(RestoreAsync_ClearsIsArchivedAndResetsStatusToPlanned));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal to restore"));
        await goals.ArchiveAsync(goal.Id);
        var restored = await goals.RestoreAsync(goal.Id);

        restored.IsArchived.Should().BeFalse();
        restored.Status.Should().Be(GoalStatus.Planned);
        restored.ArchivedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetAllArchivedAsync_ReturnsOnlyArchivedGoals()
    {
        var (goals, _, _) = BuildServices(nameof(GetAllArchivedAsync_ReturnsOnlyArchivedGoals));

        var g1 = await goals.CreateAsync(new CreateGoalDto("Active"));
        var g2 = await goals.CreateAsync(new CreateGoalDto("Archived"));
        await goals.ArchiveAsync(g2.Id);

        var archived = await goals.GetAllArchivedAsync();

        archived.Should().ContainSingle(g => g.Id == g2.Id);
        archived.Should().NotContain(g => g.Id == g1.Id);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesGoalFromStore()
    {
        var (goals, _, _) = BuildServices(nameof(DeleteAsync_RemovesGoalFromStore));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal to delete"));
        await goals.DeleteAsync(goal.Id, goal.RowVersion);

        var result = await goals.GetByIdAsync(goal.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var (goals, _, _) = BuildServices(nameof(DeleteAsync_WithStaleRowVersion_ThrowsConcurrencyConflictException));
        var goal = await goals.CreateAsync(new CreateGoalDto("Run a marathon"));

        var act = () => goals.DeleteAsync(goal.Id, [1, 2, 3]);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── Progress calculation ─────────────────────────────────────────────────

    [Fact]
    public async Task GetProgressAsync_NoMilestones_ReturnsZero()
    {
        var (goals, _, _) = BuildServices(nameof(GetProgressAsync_NoMilestones_ReturnsZero));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal with no milestones"));
        var progress = await goals.GetProgressAsync(goal.Id);

        progress.Should().Be(0);
    }

    [Fact]
    public async Task GetProgressAsync_AllMilestonesCompleted_Returns100()
    {
        var (goals, milestones, _) = BuildServices(nameof(GetProgressAsync_AllMilestonesCompleted_Returns100));

        var goal = await goals.CreateAsync(new CreateGoalDto("Fully done goal"));
        var m1 = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step 1"));
        var m2 = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step 2"));

        await milestones.CompleteAsync(m1.Id);
        await milestones.CompleteAsync(m2.Id);

        var progress = await goals.GetProgressAsync(goal.Id);
        progress.Should().Be(100);
    }

    [Fact]
    public async Task GetProgressAsync_HalfCompleted_Returns50()
    {
        var (goals, milestones, _) = BuildServices(nameof(GetProgressAsync_HalfCompleted_Returns50));

        var goal = await goals.CreateAsync(new CreateGoalDto("Half done"));
        var m1 = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step 1"));
        await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step 2"));

        await milestones.CompleteAsync(m1.Id);

        var progress = await goals.GetProgressAsync(goal.Id);
        progress.Should().Be(50);
    }

    // ── Area assignment ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByAreaAsync_ReturnsOnlyGoalsForThatArea()
    {
        var (goals, _, areas) = BuildServices(nameof(GetByAreaAsync_ReturnsOnlyGoalsForThatArea));

        var area1 = await areas.CreateAsync(new CreateAreaDto("Health"));
        var area2 = await areas.CreateAsync(new CreateAreaDto("Career"));

        var g1 = await goals.CreateAsync(new CreateGoalDto("Marathon", area1.Id));
        var g2 = await goals.CreateAsync(new CreateGoalDto("Promotion", area2.Id));

        var result = await goals.GetByAreaAsync(area1.Id);

        result.Should().ContainSingle(g => g.Id == g1.Id);
        result.Should().NotContain(g => g.Id == g2.Id);
    }

    // ── Milestone CRUD ──────────────────────────────────────────────────────

    [Fact]
    public async Task MilestoneCreateAsync_AddsToGoal()
    {
        var (goals, milestones, _) = BuildServices(nameof(MilestoneCreateAsync_AddsToGoal));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal"));
        var milestone = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step 1"));

        milestone.GoalId.Should().Be(goal.Id);
        milestone.Title.Should().Be("Step 1");
        milestone.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task MilestoneCreateAsync_ForGoalOfAnotherUser_ThrowsKeyNotFoundException()
    {
        var dbName = nameof(MilestoneCreateAsync_ForGoalOfAnotherUser_ThrowsKeyNotFoundException);
        var (goalsUser2, _, _) = BuildServices(dbName, "user-2");
        var (_, milestonesUser1, _) = BuildServices(dbName, "user-1");

        var goal = await goalsUser2.CreateAsync(new CreateGoalDto("User 2 goal"));

        var act = () => milestonesUser1.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Stolen step"));
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MilestoneCompleteAsync_SetsCompletedAtUtc()
    {
        var (goals, milestones, _) = BuildServices(nameof(MilestoneCompleteAsync_SetsCompletedAtUtc));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal"));
        var milestone = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step"));
        var completed = await milestones.CompleteAsync(milestone.Id);

        completed.IsCompleted.Should().BeTrue();
        completed.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task MilestoneUncompleteAsync_ClearsCompletedAtUtc()
    {
        var (goals, milestones, _) = BuildServices(nameof(MilestoneUncompleteAsync_ClearsCompletedAtUtc));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal"));
        var milestone = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step"));
        await milestones.CompleteAsync(milestone.Id);
        var uncompleted = await milestones.UncompleteAsync(milestone.Id);

        uncompleted.IsCompleted.Should().BeFalse();
        uncompleted.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task MilestoneDeleteAsync_RemovesMilestone()
    {
        var (goals, milestones, _) = BuildServices(nameof(MilestoneDeleteAsync_RemovesMilestone));

        var goal = await goals.CreateAsync(new CreateGoalDto("Goal"));
        var milestone = await milestones.CreateAsync(new CreateGoalMilestoneDto(goal.Id, "Step"));
        await milestones.DeleteAsync(milestone.Id);

        var remaining = await milestones.GetByGoalAsync(goal.Id);
        remaining.Should().BeEmpty();
    }

    // ── Deadline monitoring ──────────────────────────────────────────────────

    [Fact]
    public async Task GetOverdueAsync_ReturnsGoalWithPastTargetDate()
    {
        var (goals, _, _) = BuildServices(nameof(GetOverdueAsync_ReturnsGoalWithPastTargetDate));

        var goal = await goals.CreateAsync(new CreateGoalDto("Overdue goal", TargetDate: DateTime.UtcNow.AddDays(-10)));
        var overdue = await goals.GetOverdueAsync();

        overdue.Should().ContainSingle(g => g.Id == goal.Id);
    }

    [Fact]
    public async Task GetOverdueAsync_ExcludesAchievedGoals()
    {
        var (goals, _, _) = BuildServices(nameof(GetOverdueAsync_ExcludesAchievedGoals));

        var goal = await goals.CreateAsync(new CreateGoalDto("Old achieved", TargetDate: DateTime.UtcNow.AddDays(-5)));
        await goals.UpdateAsync(new UpdateGoalDto(goal.Id, goal.Title, null, null, null, GoalStatus.Achieved));

        var overdue = await goals.GetOverdueAsync();
        overdue.Should().NotContain(g => g.Id == goal.Id);
    }

    [Fact]
    public async Task GetDueSoonAsync_ReturnsFutureGoalWithinWindow()
    {
        var (goals, _, _) = BuildServices(nameof(GetDueSoonAsync_ReturnsFutureGoalWithinWindow));

        var goal = await goals.CreateAsync(new CreateGoalDto("Due soon", TargetDate: DateTime.UtcNow.AddDays(3)));
        var result = await goals.GetDueSoonAsync(daysAhead: 7);

        result.Should().ContainSingle(g => g.Id == goal.Id);
    }
}
