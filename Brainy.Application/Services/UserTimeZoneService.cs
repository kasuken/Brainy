using Brainy.Application.Common;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>Database-backed per-user calendar time-zone service.</summary>
internal sealed class UserTimeZoneService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IUserTimeZoneService
{
    public const string DefaultTimeZoneId = "UTC";

    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var stored = await context.DashboardPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Resolve(stored).Id;
    }

    public async Task<DateTime> GetUserTodayAsync(CancellationToken cancellationToken = default)
    {
        var timeZone = await GetTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        return timeProvider.GetUserToday(timeZone);
    }

    public async Task SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var validated = ResolveRequired(timeZoneId.Trim());
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
                TimeZoneId = validated.Id,
            };
            context.DashboardPreferences.Add(preference);
        }
        else if (preference.TimeZoneId == validated.Id)
        {
            return;
        }
        else
        {
            preference.TimeZoneId = validated.Id;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (isNewPreference)
        {
            context.Entry(preference).State = EntityState.Detached;
            var concurrentPreference = await context.DashboardPreferences
                .SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (concurrentPreference is null)
                throw;

            if (concurrentPreference.TimeZoneId == validated.Id)
                return;

            concurrentPreference.TimeZoneId = validated.Id;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<(DateTime StartUtc, DateTime EndUtc)> GetUtcRangeAsync(
        DateTime localStartDate,
        DateTime localEndDate,
        CancellationToken cancellationToken = default)
    {
        if (localEndDate.Date < localStartDate.Date)
            throw new ArgumentException("The end date must be on or after the start date.");

        var timeZone = await GetTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        return (
            TimeProviderExtensions.LocalDateToUtc(localStartDate, timeZone),
            TimeProviderExtensions.LocalDateToUtc(localEndDate.Date.AddDays(1), timeZone));
    }

    public async Task<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var stored = await context.DashboardPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return Resolve(stored);
    }

    private static TimeZoneInfo Resolve(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)
            ? TimeZoneInfo.Utc
            : timeZone;

    private static TimeZoneInfo ResolveRequired(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)
            ? timeZone
            : throw new ArgumentException($"'{timeZoneId}' is not a recognized time-zone id.", nameof(timeZoneId));
}
