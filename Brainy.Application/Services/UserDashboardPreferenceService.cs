using Brainy.Application.Caching;
using Brainy.Application.DTOs.Dashboard;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Manages per-user dashboard layout and threshold preferences.
/// A default record is created on first access if none exists.
/// </summary>
internal sealed class UserDashboardPreferenceService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IApplicationCache cache) : IUserDashboardPreferenceService
{
    private const int DefaultInboxWarningThreshold = 10;

    public async Task<UserDashboardPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            "dashboard:preference",
            [ApplicationCacheKey.EntityTypeTag<UserDashboardPreference>()],
            ct => GetOrCreateCoreAsync(userId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserDashboardPreferenceDto> GetOrCreateCoreAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var existing = await context.DashboardPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            return ToDto(existing);

        var created = new UserDashboardPreference
        {
            UserId = userId,
            InboxWarningThreshold = DefaultInboxWarningThreshold,
        };

        context.DashboardPreferences.Add(created);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidatePreferenceAsync(userId, created.Id).ConfigureAwait(false);

        return ToDto(created);
    }

    public async Task<UserDashboardPreferenceDto> UpdateAsync(
        UpdateDashboardPreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var preference = await context.DashboardPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = new UserDashboardPreference { UserId = userId };
            context.DashboardPreferences.Add(preference);
        }

        preference.WidgetOrder = dto.WidgetOrder;
        preference.CollapsedWidgets = dto.CollapsedWidgets;
        preference.InboxWarningThreshold = dto.InboxWarningThreshold;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidatePreferenceAsync(userId, preference.Id).ConfigureAwait(false);

        return ToDto(preference);
    }

    public Task<UserDashboardPreferenceDto> SetStarterModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        MutateAsync(p => p.StarterModeEnabled = enabled, cancellationToken);

    public Task<UserDashboardPreferenceDto> SetOnboardingStepAsync(
        int step,
        CancellationToken cancellationToken = default) =>
        MutateAsync(p => p.OnboardingStep = Math.Max(0, step), cancellationToken);

    public Task<UserDashboardPreferenceDto> CompleteOnboardingAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(p => p.OnboardingCompleted = true, cancellationToken);

    public Task<UserDashboardPreferenceDto> DismissOnboardingAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(p => p.OnboardingDismissed = true, cancellationToken);

    public Task<UserDashboardPreferenceDto> ResetOnboardingAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(
            p =>
            {
                p.OnboardingCompleted = false;
                p.OnboardingDismissed = false;
                p.OnboardingStep = 0;
            },
            cancellationToken);

    private async Task<UserDashboardPreferenceDto> MutateAsync(
        Action<UserDashboardPreference> mutate,
        CancellationToken cancellationToken)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var preference = await context.DashboardPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = new UserDashboardPreference { UserId = userId };
            context.DashboardPreferences.Add(preference);
        }

        mutate(preference);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await InvalidatePreferenceAsync(userId, preference.Id).ConfigureAwait(false);

        return ToDto(preference);
    }

    private static UserDashboardPreferenceDto ToDto(UserDashboardPreference p) =>
        new(
            p.Id,
            p.WidgetOrder,
            p.CollapsedWidgets,
            p.InboxWarningThreshold,
            p.StarterModeEnabled,
            p.OnboardingCompleted,
            p.OnboardingDismissed,
            p.OnboardingStep);

    private ValueTask InvalidatePreferenceAsync(string userId, Guid preferenceId) =>
        cache.InvalidateTagsAsync(
            userId,
            [
                ApplicationCacheKey.EntityTypeTag<UserDashboardPreference>(),
                ApplicationCacheKey.EntityTag<UserDashboardPreference>(preferenceId)
            ],
            CancellationToken.None);
}
