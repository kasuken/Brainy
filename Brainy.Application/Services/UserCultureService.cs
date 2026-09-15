using Brainy.Application.Caching;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Localization;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>Database-backed per-user UI culture (language) preference service.</summary>
internal sealed class UserCultureService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IUserCultureService
{
    public async Task<string?> GetCultureIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await cache.GetOrCreateAsync(
            userId,
            "dashboard:culture",
            [
                ApplicationCacheKey.EntityTypeTag<UserDashboardPreference>(),
                ApplicationCacheKey.CultureTag
            ],
            ct => context.DashboardPreferences
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => p.CultureId)
                .FirstOrDefaultAsync(ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCultureIdAsync(string cultureId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureId);
        var trimmed = cultureId.Trim();
        if (!SupportedCultures.IsSupported(trimmed))
            throw new ArgumentException($"'{cultureId}' is not a supported culture.", nameof(cultureId));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var preference = await context.DashboardPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        var isNewPreference = preference is null;

        if (preference is null)
        {
            preference = new UserDashboardPreference
            {
                UserId = userId,
                CultureId = trimmed,
            };
            context.DashboardPreferences.Add(preference);
        }
        else if (preference.CultureId == trimmed)
        {
            return;
        }
        else
        {
            preference.CultureId = trimmed;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateCultureAsync(userId, preference.Id).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (isNewPreference)
        {
            context.Entry(preference).State = EntityState.Detached;
            var concurrentPreference = await context.DashboardPreferences
                .SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (concurrentPreference is null)
                throw;

            if (concurrentPreference.CultureId == trimmed)
                return;

            concurrentPreference.CultureId = trimmed;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidateCultureAsync(userId, concurrentPreference.Id).ConfigureAwait(false);
        }
    }

    private ValueTask InvalidateCultureAsync(string userId, Guid preferenceId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<UserDashboardPreference>(),
                ApplicationCacheKey.EntityTag<UserDashboardPreference>(preferenceId),
                ApplicationCacheKey.CultureTag
            ],
            CancellationToken.None);
}
