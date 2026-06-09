using Brainy.Application.DTOs.Para;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Fetches PARA category counts in parallel for efficient dashboard display.
/// All queries use <c>AsNoTracking</c> and are scoped to the current user.
/// </summary>
internal sealed class ParaSummaryService(IApplicationDbContext context, ICurrentUserService currentUser)
    : IParaSummaryService
{
    public async Task<ParaSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // EF Core DbContext is not thread-safe; queries must run sequentially.
        var activeProjects   = await context.Projects.AsNoTracking().CountAsync(p => p.UserId == userId && !p.IsArchived, cancellationToken).ConfigureAwait(false);
        var archivedProjects = await context.Projects.AsNoTracking().CountAsync(p => p.UserId == userId && p.IsArchived, cancellationToken).ConfigureAwait(false);
        var activeAreas      = await context.Areas.AsNoTracking().CountAsync(a => a.UserId == userId && !a.IsArchived, cancellationToken).ConfigureAwait(false);
        var archivedAreas    = await context.Areas.AsNoTracking().CountAsync(a => a.UserId == userId && a.IsArchived, cancellationToken).ConfigureAwait(false);
        var activeResources  = await context.Resources.AsNoTracking().CountAsync(r => r.UserId == userId && !r.IsArchived, cancellationToken).ConfigureAwait(false);
        var archivedResources = await context.Resources.AsNoTracking().CountAsync(r => r.UserId == userId && r.IsArchived, cancellationToken).ConfigureAwait(false);

        return new ParaSummaryDto(
            ActiveProjectCount:    activeProjects,
            ArchivedProjectCount:  archivedProjects,
            ActiveAreaCount:       activeAreas,
            ArchivedAreaCount:     archivedAreas,
            ActiveResourceCount:   activeResources,
            ArchivedResourceCount: archivedResources);
    }
}
