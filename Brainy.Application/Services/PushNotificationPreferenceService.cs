using Brainy.Application.DTOs.Push;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IPushNotificationPreferenceService"/>. A row is created only when
/// the user first saves a preference; until then <see cref="GetOrCreateAsync"/> returns the
/// off-by-default values without writing anything, so simply opening the settings page never
/// opts a user in.
/// </summary>
internal sealed class PushNotificationPreferenceService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : IPushNotificationPreferenceService
{
    public async Task<PushNotificationPreferenceDto> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.PushNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return existing is null ? ToDto(new PushNotificationPreference()) : ToDto(existing);
    }

    public async Task<PushNotificationPreferenceDto> UpdateAsync(
        UpdatePushNotificationPreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.DailyFocusNudgeHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(dto), "Daily focus nudge hour must be between 0 and 23.");
        if (dto.WeeklyReviewHour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(dto), "Weekly review hour must be between 0 and 23.");

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var preference = await context.PushNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = new PushNotificationPreference { UserId = userId };
            context.PushNotificationPreferences.Add(preference);
        }

        preference.Enabled = dto.Enabled;
        preference.DailyFocusNudgeEnabled = dto.DailyFocusNudgeEnabled;
        preference.OverdueTaskEnabled = dto.OverdueTaskEnabled;
        preference.WeeklyReviewReminderEnabled = dto.WeeklyReviewReminderEnabled;
        preference.QuietHoursEnabled = dto.QuietHoursEnabled;
        preference.QuietHoursStart = dto.QuietHoursStart;
        preference.QuietHoursEnd = dto.QuietHoursEnd;
        preference.DailyFocusNudgeHour = dto.DailyFocusNudgeHour;
        preference.WeeklyReviewDayOfWeek = dto.WeeklyReviewDayOfWeek;
        preference.WeeklyReviewHour = dto.WeeklyReviewHour;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(preference);
    }

    private static PushNotificationPreferenceDto ToDto(PushNotificationPreference p) =>
        new(
            p.Enabled,
            p.DailyFocusNudgeEnabled,
            p.OverdueTaskEnabled,
            p.WeeklyReviewReminderEnabled,
            p.QuietHoursEnabled,
            p.QuietHoursStart,
            p.QuietHoursEnd,
            p.DailyFocusNudgeHour,
            p.WeeklyReviewDayOfWeek,
            p.WeeklyReviewHour);
}
