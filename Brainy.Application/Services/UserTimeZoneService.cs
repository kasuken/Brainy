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
    private const string ManualOverridePrefix = "manual:";

    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default)
    {
        return Resolve(await GetStoredTimeZoneValueAsync(cancellationToken).ConfigureAwait(false)).Id;
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
        await SetStoredTimeZoneValueAsync(validated.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetTimeZoneOverrideIdAsync(CancellationToken cancellationToken = default)
    {
        var stored = await GetStoredTimeZoneValueAsync(cancellationToken).ConfigureAwait(false);
        return TryResolveOverride(stored, out var timeZone) ? timeZone.Id : null;
    }

    public async Task SetTimeZoneOverrideAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var validated = ResolveRequired(timeZoneId.Trim());
        await SetStoredTimeZoneValueAsync($"{ManualOverridePrefix}{validated.Id}", cancellationToken).ConfigureAwait(false);
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
        return Resolve(await GetStoredTimeZoneValueAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<string?> GetStoredTimeZoneValueAsync(CancellationToken cancellationToken)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await context.DashboardPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SetStoredTimeZoneValueAsync(string storedTimeZoneValue, CancellationToken cancellationToken)
    {
        var effectiveTimeZone = ResolveRequired(ExtractEffectiveTimeZoneId(storedTimeZoneValue) ?? storedTimeZoneValue);
        var normalizedStoredValue = storedTimeZoneValue.StartsWith(ManualOverridePrefix, StringComparison.Ordinal)
            ? $"{ManualOverridePrefix}{effectiveTimeZone.Id}"
            : effectiveTimeZone.Id;
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
                TimeZoneId = normalizedStoredValue,
            };
            context.DashboardPreferences.Add(preference);
        }
        else if (preference.TimeZoneId == normalizedStoredValue)
        {
            return;
        }
        else
        {
            preference.TimeZoneId = normalizedStoredValue;
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

            if (concurrentPreference.TimeZoneId == normalizedStoredValue)
                return;

            concurrentPreference.TimeZoneId = normalizedStoredValue;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var effectiveTimeZoneId = ExtractEffectiveTimeZoneId(timeZoneId);
        return string.IsNullOrWhiteSpace(effectiveTimeZoneId) ||
               !TimeZoneInfo.TryFindSystemTimeZoneById(effectiveTimeZoneId, out var timeZone)
            ? TimeZoneInfo.Utc
            : timeZone;
    }

    private static TimeZoneInfo ResolveRequired(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone)
            ? timeZone
            : throw new ArgumentException($"'{timeZoneId}' is not a recognized time-zone id.", nameof(timeZoneId));

    private static bool TryResolveOverride(string? storedTimeZoneId, out TimeZoneInfo timeZone)
    {
        var overrideId = storedTimeZoneId is not null && storedTimeZoneId.StartsWith(ManualOverridePrefix, StringComparison.Ordinal)
            ? storedTimeZoneId[ManualOverridePrefix.Length..]
            : null;
        if (!string.IsNullOrWhiteSpace(overrideId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(overrideId, out var resolvedTimeZone))
        {
            timeZone = resolvedTimeZone;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static string? ExtractEffectiveTimeZoneId(string? storedTimeZoneId) =>
        storedTimeZoneId is not null && storedTimeZoneId.StartsWith(ManualOverridePrefix, StringComparison.Ordinal)
            ? storedTimeZoneId[ManualOverridePrefix.Length..]
            : storedTimeZoneId;
}
