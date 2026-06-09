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

        var activeProjectsTask  = context.Projects.AsNoTracking().CountAsync(p => p.UserId == userId && !p.IsArchived, cancellationToken);
        var archivedProjectsTask = context.Projects.AsNoTracking().CountAsync(p => p.UserId == userId && p.IsArchived, cancellationToken);
        var activeAreasTask     = context.Areas.AsNoTracking().CountAsync(a => a.UserId == userId && !a.IsArchived, cancellationToken);
        var archivedAreasTask   = context.Areas.AsNoTracking().CountAsync(a => a.UserId == userId && a.IsArchived, cancellationToken);
        var activeResourcesTask = context.Resources.AsNoTracking().CountAsync(r => r.UserId == userId && !r.IsArchived, cancellationToken);
        var archivedResourcesTask = context.Resources.AsNoTracking().CountAsync(r => r.UserId == userId && r.IsArchived, cancellationToken);

        await Task.WhenAll(
            activeProjectsTask, archivedProjectsTask,
            activeAreasTask, archivedAreasTask,
            activeResourcesTask, archivedResourcesTask)
            .ConfigureAwait(false);

        return new ParaSummaryDto(
            ActiveProjectCount:   activeProjectsTask.Result,
            ArchivedProjectCount: archivedProjectsTask.Result,
            ActiveAreaCount:      activeAreasTask.Result,
            ArchivedAreaCount:    archivedAreasTask.Result,
            ActiveResourceCount:  activeResourcesTask.Result,
            ArchivedResourceCount: archivedResourcesTask.Result);
    }
}
