using Brainy.Application.DTOs.Dashboard;
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
    ICurrentUserService currentUser) : IUserDashboardPreferenceService
{
    private const int DefaultInboxWarningThreshold = 10;

    public async Task<UserDashboardPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

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

        return ToDto(preference);
    }

    private static UserDashboardPreferenceDto ToDto(UserDashboardPreference p) =>
        new(p.Id, p.WidgetOrder, p.CollapsedWidgets, p.InboxWarningThreshold);
}
