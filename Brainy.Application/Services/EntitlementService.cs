using Brainy.Application.Analytics;
using Brainy.Application.Billing;
using Brainy.Application.DTOs.Billing;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Enforces the plan matrix defined in <see cref="PlanCatalog"/>. This is the only code path
/// allowed to grant or deny a paid-only action, and the only code path allowed to mutate
/// <see cref="Domain.Entities.Project.IsReadOnly"/> or a user's <see cref="UserPlan"/>.
/// </summary>
internal sealed class EntitlementService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IAnalyticsService analytics,
    TimeProvider timeProvider) : IEntitlementService
{
    public async Task<EntitlementCheckResult> CanCreateProjectAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var plan = PlanCatalog.Get(await GetPlanTierAsync(userId, cancellationToken).ConfigureAwait(false));

        if (plan.MaxActiveProjects is null)
            return EntitlementCheckResult.Allow();

        var activeCount = await CountNonArchivedProjectsAsync(userId, cancellationToken).ConfigureAwait(false);
        if (activeCount < plan.MaxActiveProjects.Value)
            return EntitlementCheckResult.Allow();

        await TrackLimitReachedAsync(userId, "active_projects", plan, cancellationToken).ConfigureAwait(false);
        return EntitlementCheckResult.Deny(
            $"Your {plan.DisplayName} plan allows up to {plan.MaxActiveProjects.Value} active projects. " +
            "Archive an existing project or upgrade to add another.");
    }

    public async Task<AiAllowanceStatus> GetAiAllowanceAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var userPlan = await GetOrCreateUserPlanAsync(userId, cancellationToken).ConfigureAwait(false);
        return BuildAllowanceStatus(userPlan, PlanCatalog.Get(userPlan.Tier));
    }

    public async Task<EntitlementCheckResult> TryConsumeAiAllowanceAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var userPlan = await GetOrCreateUserPlanAsync(userId, cancellationToken).ConfigureAwait(false);
        var plan = PlanCatalog.Get(userPlan.Tier);

        if (!plan.AiAvailable)
        {
            await TrackLimitReachedAsync(userId, "ai_not_available", plan, cancellationToken).ConfigureAwait(false);
            return EntitlementCheckResult.Deny(
                $"AI features are not included on the {plan.DisplayName} plan. Upgrade to Pro to use AI.");
        }

        ResetPeriodIfElapsed(userPlan, plan);

        if (plan.AiAllowancePerPeriod is not null && userPlan.AiAllowanceUsedInPeriod >= plan.AiAllowancePerPeriod.Value)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await TrackLimitReachedAsync(userId, "ai_allowance", plan, cancellationToken).ConfigureAwait(false);
            return EntitlementCheckResult.Deny(
                $"You've used this period's AI allowance ({plan.AiAllowancePerPeriod.Value} requests). " +
                "It resets automatically, or upgrade for a higher allowance.");
        }

        userPlan.AiAllowanceUsedInPeriod++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return EntitlementCheckResult.Allow();
    }

    public async Task<PlanUsageSummaryDto> GetUsageSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var userPlan = await GetOrCreateUserPlanAsync(userId, cancellationToken).ConfigureAwait(false);
        var plan = PlanCatalog.Get(userPlan.Tier);

        var activeProjectCount = await CountNonArchivedProjectsAsync(userId, cancellationToken).ConfigureAwait(false);
        var readOnlyCount = await context.Projects.AsNoTracking()
            .CountAsync(p => p.UserId == userId && !p.IsArchived && p.IsReadOnly, cancellationToken)
            .ConfigureAwait(false);

        return new PlanUsageSummaryDto(
            plan.Tier,
            plan.DisplayName,
            plan.PriceDescription,
            activeProjectCount,
            plan.MaxActiveProjects,
            plan.MaxActiveProjects.HasValue && activeProjectCount >= plan.MaxActiveProjects.Value,
            readOnlyCount,
            BuildAllowanceStatus(userPlan, plan),
            userPlan.PlanRenewsAtUtc,
            userPlan.TrialEndsAtUtc,
            userPlan.GracePeriodEndsAtUtc);
    }

    public async Task ReconcileProjectAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var plan = PlanCatalog.Get(await GetPlanTierAsync(userId, cancellationToken).ConfigureAwait(false));

        var projects = await context.Projects
            .Where(p => p.UserId == userId && !p.IsArchived && p.Status != Domain.Enums.ProjectStatus.Archived)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Oldest-created projects beyond the cap become read-only; the most recently
        // created projects (the ones a user is likeliest still working on) stay writable.
        var overflow = plan.MaxActiveProjects.HasValue
            ? Math.Max(0, projects.Count - plan.MaxActiveProjects.Value)
            : 0;

        for (var i = 0; i < projects.Count; i++)
            projects[i].IsReadOnly = i < overflow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPlanTierAsync(
        string userId,
        PlanTier tier,
        DateTime? planRenewsAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var userPlan = await GetOrCreateUserPlanAsync(userId, cancellationToken).ConfigureAwait(false);
        var tierUnchanged = userPlan.Tier == tier;

        userPlan.Tier = tier;
        if (planRenewsAtUtc.HasValue)
            userPlan.PlanRenewsAtUtc = planRenewsAtUtc;

        if (tierUnchanged && !planRenewsAtUtc.HasValue)
            return;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (tierUnchanged)
            return;

        await ReconcileProjectAccessAsync(userId, cancellationToken).ConfigureAwait(false);

        await analytics.TrackAsync(
            userId,
            AnalyticsEvents.PlanConverted,
            new Dictionary<string, object?> { ["tier"] = tier.ToString() },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlanTier> GetPlanTierAsync(string userId, CancellationToken cancellationToken)
    {
        var tier = await context.UserPlans.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (PlanTier?)p.Tier)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // No row yet means the user has never had a plan change recorded: default Starter.
        return tier ?? PlanTier.Starter;
    }

    private async Task<UserPlan> GetOrCreateUserPlanAsync(string userId, CancellationToken cancellationToken)
    {
        var userPlan = await context.UserPlans
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (userPlan is not null)
            return userPlan;

        userPlan = new UserPlan { UserId = userId, Tier = PlanTier.Starter };
        context.UserPlans.Add(userPlan);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return userPlan;
    }

    private Task<int> CountNonArchivedProjectsAsync(string userId, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking()
            .CountAsync(p => p.UserId == userId && !p.IsArchived && p.Status != Domain.Enums.ProjectStatus.Archived, cancellationToken);

    /// <summary>Rolls the AI-allowance counter over to a fresh period if the current one elapsed. Does not save.</summary>
    private void ResetPeriodIfElapsed(UserPlan userPlan, PlanDefinition plan)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (userPlan.AiAllowancePeriodStartUtc is null || now >= userPlan.AiAllowancePeriodStartUtc.Value + plan.AiAllowancePeriod)
        {
            userPlan.AiAllowancePeriodStartUtc = now;
            userPlan.AiAllowanceUsedInPeriod = 0;
        }
    }

    private AiAllowanceStatus BuildAllowanceStatus(UserPlan userPlan, PlanDefinition plan)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var periodStart = userPlan.AiAllowancePeriodStartUtc ?? now;
        var periodElapsed = now >= periodStart + plan.AiAllowancePeriod;
        var used = periodElapsed ? 0 : userPlan.AiAllowanceUsedInPeriod;
        var resetsAt = (periodElapsed ? now : periodStart) + plan.AiAllowancePeriod;

        if (!plan.AiAvailable)
            return new AiAllowanceStatus(false, false, 0, used, resetsAt, plan.AllowsBringYourOwnKey);

        if (plan.AiAllowancePerPeriod is null)
            return new AiAllowanceStatus(true, true, null, used, resetsAt, plan.AllowsBringYourOwnKey);

        return new AiAllowanceStatus(true, used < plan.AiAllowancePerPeriod.Value, plan.AiAllowancePerPeriod, used, resetsAt, plan.AllowsBringYourOwnKey);
    }

    private Task TrackLimitReachedAsync(string userId, string limitKind, PlanDefinition plan, CancellationToken cancellationToken) =>
        analytics.TrackAsync(
            userId,
            AnalyticsEvents.PlanLimitReached,
            new Dictionary<string, object?> { ["limit"] = limitKind, ["plan"] = plan.Tier.ToString() },
            cancellationToken);
}
