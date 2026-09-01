using Brainy.Application.Caching;
using Brainy.Application.DTOs.Goals;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages <see cref="GoalMilestone"/> entities.
/// All milestone operations verify that the parent <see cref="Goal"/> belongs to the current user.
/// </summary>
internal sealed class GoalMilestoneService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IGoalMilestoneService
{
    public async Task<IReadOnlyList<GoalMilestoneDto>> GetByGoalAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"goal-milestones:goal:{goalId}",
            [
                ApplicationCacheKey.EntityTypeTag<GoalMilestone>(),
                ApplicationCacheKey.EntityTypeTag<Goal>(),
                ApplicationCacheKey.EntityTag<Goal>(goalId)
            ],
            async ct => await context.GoalMilestones
                .AsNoTracking()
                .Where(m => m.GoalId == goalId && m.Goal!.UserId == userId)
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => ToDto(m))
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoalMilestoneDto> CreateAsync(CreateGoalMilestoneDto dto, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var goalExists = await context.Goals
            .AnyAsync(g => g.Id == dto.GoalId && g.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!goalExists)
            throw new KeyNotFoundException($"Goal {dto.GoalId} not found.");

        var milestone = new GoalMilestone
        {
            Id = Guid.NewGuid(),
            GoalId = dto.GoalId,
            Title = dto.Title
        };

        context.GoalMilestones.Add(milestone);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateMilestoneAsync(userId, milestone.Id).ConfigureAwait(false);

        return ToDto(milestone);
    }

    public async Task<GoalMilestoneDto> UpdateAsync(UpdateGoalMilestoneDto dto, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var milestone = await context.GoalMilestones
            .Where(m => m.Id == dto.Id && m.Goal!.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Milestone {dto.Id} not found.");

        milestone.Title = dto.Title;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateMilestoneAsync(userId, milestone.Id).ConfigureAwait(false);

        return ToDto(milestone);
    }

    public async Task<GoalMilestoneDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var milestone = await context.GoalMilestones
            .Where(m => m.Id == id && m.Goal!.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Milestone {id} not found.");

        milestone.IsCompleted = true;
        milestone.CompletedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateMilestoneAsync(userId, milestone.Id).ConfigureAwait(false);

        return ToDto(milestone);
    }

    public async Task<GoalMilestoneDto> UncompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var milestone = await context.GoalMilestones
            .Where(m => m.Id == id && m.Goal!.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Milestone {id} not found.");

        milestone.IsCompleted = false;
        milestone.CompletedAtUtc = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateMilestoneAsync(userId, milestone.Id).ConfigureAwait(false);

        return ToDto(milestone);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var milestone = await context.GoalMilestones
            .Where(m => m.Id == id && m.Goal!.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Milestone {id} not found.");

        context.GoalMilestones.Remove(milestone);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidateMilestoneAsync(userId, milestone.Id).ConfigureAwait(false);
    }

    private ValueTask InvalidateMilestoneAsync(string userId, Guid milestoneId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<GoalMilestone>(),
                ApplicationCacheKey.EntityTag<GoalMilestone>(milestoneId)
            ],
            CancellationToken.None);

    private static GoalMilestoneDto ToDto(GoalMilestone m) =>
        new(m.Id, m.GoalId, m.Title, m.IsCompleted, m.CompletedAtUtc, m.CreatedAtUtc);
}
