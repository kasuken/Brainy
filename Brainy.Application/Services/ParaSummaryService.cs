using Brainy.Application.DTOs.Para;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Fetches PARA category counts for the dashboard: one grouped query per entity type.
/// All queries use <c>AsNoTracking</c> and are scoped to the current user.
/// </summary>
internal sealed class ParaSummaryService(IApplicationDbContext context, ICurrentUserService currentUser)
    : IParaSummaryService
{
    public async Task<ParaSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // One GROUP BY query per entity type (3 round-trips instead of 6 counts).
        // EF Core DbContext is not thread-safe; queries must run sequentially.
        var projectCounts = await context.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .GroupBy(p => p.IsArchived)
            .Select(g => new { Archived = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var areaCounts = await context.Areas.AsNoTracking()
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.IsArchived)
            .Select(g => new { Archived = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var resourceCounts = await context.Resources.AsNoTracking()
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.IsArchived)
            .Select(g => new { Archived = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ParaSummaryDto(
            ActiveProjectCount:    projectCounts.FirstOrDefault(c => !c.Archived)?.Count ?? 0,
            ArchivedProjectCount:  projectCounts.FirstOrDefault(c => c.Archived)?.Count ?? 0,
            ActiveAreaCount:       areaCounts.FirstOrDefault(c => !c.Archived)?.Count ?? 0,
            ArchivedAreaCount:     areaCounts.FirstOrDefault(c => c.Archived)?.Count ?? 0,
            ActiveResourceCount:   resourceCounts.FirstOrDefault(c => !c.Archived)?.Count ?? 0,
            ArchivedResourceCount: resourceCounts.FirstOrDefault(c => c.Archived)?.Count ?? 0);
    }
}
