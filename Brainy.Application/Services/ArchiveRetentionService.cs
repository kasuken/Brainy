using Brainy.Application.DTOs.Archives;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

internal sealed class ArchiveRetentionService(IApplicationDbContext context, ICurrentUserService currentUser) : IArchiveRetentionService
{
    public async Task<IReadOnlyList<ArchiveRetentionRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await context.ArchiveRetentionRules
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.EntityType)
            .Select(r => new ArchiveRetentionRuleDto(r.Id, r.EntityType, r.RetentionDays))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertRuleAsync(string entityType, int? retentionDays, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.ArchiveRetentionRules
            .FirstOrDefaultAsync(r => r.UserId == userId && r.EntityType == entityType, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.ArchiveRetentionRules.Add(new ArchiveRetentionRule
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EntityType = entityType,
                RetentionDays = retentionDays
            });
        }
        else
        {
            existing.RetentionDays = retentionDays;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRuleAsync(string entityType, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var rule = await context.ArchiveRetentionRules
            .FirstOrDefaultAsync(r => r.UserId == userId && r.EntityType == entityType, cancellationToken)
            .ConfigureAwait(false);
        if (rule is not null)
        {
            context.ArchiveRetentionRules.Remove(rule);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Enforcement across all users requires a system-level (non-scoped) user identity.
    /// Returns 0 until a system identity provider is wired up.
    /// </remarks>
    public Task<int> EnforceRetentionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}