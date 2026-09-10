using Brainy.Application.Common;
using Brainy.Application.DTOs.Areas;
using Brainy.Application.DTOs.Projects;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
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
/// Entitlement-boundary tests for issue #296: the Starter active-project cap, Starter's
/// lack of AI availability, Pro's AI allowance, and downgrade reconciliation flipping the
/// oldest-created over-cap projects read-only.
/// </summary>
public sealed class EntitlementServiceTests
{
    private const string UserId = "entitlement-user";

    private static (IEntitlementService Entitlements, IProjectService Projects, IAreaService Areas, BrainyDbContext Db) BuildServices(
        string dbName, string userId = UserId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BrainyDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BrainyDbContext>());
        services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService(userId));
        services.AddBrainyApplication();

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IEntitlementService>(),
            provider.GetRequiredService<IProjectService>(),
            provider.GetRequiredService<IAreaService>(),
            provider.GetRequiredService<BrainyDbContext>());
    }

    // ── Active-project cap ───────────────────────────────────────────────────

    [Fact]
    public async Task CanCreateProjectAsync_OnStarter_AllowsUpToThreeThenDenies()
    {
        var (entitlements, projects, areas, _) = BuildServices(nameof(CanCreateProjectAsync_OnStarter_AllowsUpToThreeThenDenies));
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        for (var i = 0; i < 3; i++)
        {
            (await entitlements.CanCreateProjectAsync()).IsAllowed.Should().BeTrue();
            await projects.CreateAsync(new CreateProjectDto($"Project {i}", area.Id));
        }

        var result = await entitlements.CanCreateProjectAsync();
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Starter");

        await projects.Invoking(p => p.CreateAsync(new CreateProjectDto("One too many", area.Id)))
            .Should().ThrowAsync<PlanEntitlementDeniedException>();
    }

    [Fact]
    public async Task CanCreateProjectAsync_OnPro_IsUnlimited()
    {
        var (entitlements, projects, areas, _) = BuildServices(nameof(CanCreateProjectAsync_OnPro_IsUnlimited));
        await entitlements.SetPlanTierAsync(UserId, PlanTier.Pro);
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        for (var i = 0; i < 5; i++)
            await projects.CreateAsync(new CreateProjectDto($"Project {i}", area.Id));

        (await entitlements.CanCreateProjectAsync()).IsAllowed.Should().BeTrue();
    }

    // ── AI availability / allowance ──────────────────────────────────────────

    [Fact]
    public async Task TryConsumeAiAllowanceAsync_OnStarter_IsDenied()
    {
        var (entitlements, _, _, _) = BuildServices(nameof(TryConsumeAiAllowanceAsync_OnStarter_IsDenied));

        var status = await entitlements.GetAiAllowanceAsync();
        status.AiAvailableOnPlan.Should().BeFalse();
        status.HasAllowanceRemaining.Should().BeFalse();

        var result = await entitlements.TryConsumeAiAllowanceAsync();
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Pro");
    }

    [Fact]
    public async Task TryConsumeAiAllowanceAsync_OnPro_IsAllowedAndTracksUsage()
    {
        var (entitlements, _, _, _) = BuildServices(nameof(TryConsumeAiAllowanceAsync_OnPro_IsAllowedAndTracksUsage));
        await entitlements.SetPlanTierAsync(UserId, PlanTier.Pro);

        (await entitlements.TryConsumeAiAllowanceAsync()).IsAllowed.Should().BeTrue();
        (await entitlements.TryConsumeAiAllowanceAsync()).IsAllowed.Should().BeTrue();

        var status = await entitlements.GetAiAllowanceAsync();
        status.AiAvailableOnPlan.Should().BeTrue();
        status.Used.Should().Be(2);
        status.Limit.Should().BeNull(); // unlimited-for-now default, per issue #296's open metering question
    }

    // ── Downgrade reconciliation ──────────────────────────────────────────────

    [Fact]
    public async Task ReconcileProjectAccessAsync_OnDowngrade_FlipsOldestOverCapProjectsReadOnly()
    {
        var dbName = nameof(ReconcileProjectAccessAsync_OnDowngrade_FlipsOldestOverCapProjectsReadOnly);
        var (entitlements, projects, areas, db) = BuildServices(dbName);
        await entitlements.SetPlanTierAsync(UserId, PlanTier.Pro);
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        var created = new List<ProjectDto>();
        for (var i = 0; i < 5; i++)
            created.Add(await projects.CreateAsync(new CreateProjectDto($"Project {i}", area.Id)));

        // Force distinct, ascending CreatedAtUtc so "oldest" ordering is unambiguous
        // (the InMemory provider does not otherwise guarantee ordering granularity).
        for (var i = 0; i < created.Count; i++)
        {
            var entity = await db.Projects.FirstAsync(p => p.Id == created[i].Id);
            entity.CreatedAtUtc = DateTime.UtcNow.AddMinutes(i);
        }
        await db.SaveChangesAsync();

        await entitlements.SetPlanTierAsync(UserId, PlanTier.Starter);

        var afterDowngrade = await db.Projects.AsNoTracking()
            .Where(p => p.UserId == UserId)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync();

        afterDowngrade.Take(2).Should().OnlyContain(p => p.IsReadOnly);
        afterDowngrade.Skip(2).Should().OnlyContain(p => !p.IsReadOnly);

        var usage = await entitlements.GetUsageSummaryAsync();
        usage.ReadOnlyProjectCount.Should().Be(2);
        usage.ProjectLimitReached.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_OnReadOnlyProject_ThrowsProjectReadOnlyException()
    {
        var dbName = nameof(UpdateAsync_OnReadOnlyProject_ThrowsProjectReadOnlyException);
        var (entitlements, projects, areas, db) = BuildServices(dbName);
        await entitlements.SetPlanTierAsync(UserId, PlanTier.Pro);
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        // Starter's cap is 3, so a 4th project is needed to force one project over the limit.
        var project = await projects.CreateAsync(new CreateProjectDto("Oldest (will go over the limit)", area.Id));
        var others = new List<ProjectDto>
        {
            await projects.CreateAsync(new CreateProjectDto("Project 2", area.Id)),
            await projects.CreateAsync(new CreateProjectDto("Project 3", area.Id)),
            await projects.CreateAsync(new CreateProjectDto("Project 4", area.Id)),
        };

        var allIds = new[] { project.Id }.Concat(others.Select(p => p.Id)).ToList();
        for (var i = 0; i < allIds.Count; i++)
        {
            var entity = await db.Projects.FirstAsync(p => p.Id == allIds[i]);
            entity.CreatedAtUtc = DateTime.UtcNow.AddMinutes(i);
        }
        await db.SaveChangesAsync();

        await entitlements.SetPlanTierAsync(UserId, PlanTier.Starter);

        var reloaded = await projects.GetByIdAsync(project.Id);
        reloaded!.IsReadOnly.Should().BeTrue();

        await projects.Invoking(p => p.UpdateAsync(new UpdateProjectDto(
                project.Id, "Renamed", area.Id, null, null,
                ProjectStatus.NotStarted, ProjectPriority.Medium, null, null)))
            .Should().ThrowAsync<ProjectReadOnlyException>();
    }

    [Fact]
    public async Task ArchiveAsync_FreesCapacity_AndUnfreezesRemainingProjects()
    {
        var dbName = nameof(ArchiveAsync_FreesCapacity_AndUnfreezesRemainingProjects);
        var (entitlements, projects, areas, db) = BuildServices(dbName);
        await entitlements.SetPlanTierAsync(UserId, PlanTier.Pro);
        var area = await areas.CreateAsync(new CreateAreaDto("Work"));

        var created = new List<ProjectDto>();
        for (var i = 0; i < 4; i++)
            created.Add(await projects.CreateAsync(new CreateProjectDto($"Project {i}", area.Id)));

        for (var i = 0; i < created.Count; i++)
        {
            var entity = await db.Projects.FirstAsync(p => p.Id == created[i].Id);
            entity.CreatedAtUtc = DateTime.UtcNow.AddMinutes(i);
        }
        await db.SaveChangesAsync();

        await entitlements.SetPlanTierAsync(UserId, PlanTier.Starter);
        (await entitlements.GetUsageSummaryAsync()).ReadOnlyProjectCount.Should().Be(1);

        // Archiving the oldest (read-only) project frees capacity for the rest.
        await projects.ArchiveAsync(created[0].Id);

        var remaining = await db.Projects.AsNoTracking()
            .Where(p => p.UserId == UserId && !p.IsArchived)
            .ToListAsync();
        remaining.Should().OnlyContain(p => !p.IsReadOnly);
    }
}
