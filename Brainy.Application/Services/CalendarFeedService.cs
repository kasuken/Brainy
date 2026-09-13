using Brainy.Application.Caching;
using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="ICalendarFeedService"/>. Every query below is explicitly scoped to
/// the <c>userId</c> parameter (never an ambient "current user") and always excludes archived
/// and completed/done items, matching <c>CalendarService</c>'s active-workflow rules.
/// </summary>
internal sealed class CalendarFeedService(
    IApplicationDbContext context,
    IApplicationCache cache) : ICalendarFeedService
{
    public async Task<IReadOnlyList<CalendarFeedEventDto>> GetFeedEventsAsync(
        string userId,
        CalendarFilterDto? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var cacheKey = ApplicationCacheKey.Create(
            "calendar-feed",
            filter?.ProjectId,
            filter?.AreaId,
            filter?.Priority,
            filter?.Status,
            filter?.SearchTerm);

        return await cache.GetOrCreateAsync(
            userId,
            cacheKey,
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>(),
                ApplicationCacheKey.EntityTypeTag<GoalMilestone>(),
                ApplicationCacheKey.EntityTypeTag<Goal>()
            ],
            ct => GetFeedEventsCoreAsync(userId, filter, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CalendarFeedEventDto>> GetFeedEventsCoreAsync(
        string userId,
        CalendarFilterDto? filter,
        CancellationToken cancellationToken)
    {
        var events = new List<CalendarFeedEventDto>();
        events.AddRange(await GetTaskEventsAsync(userId, filter, cancellationToken).ConfigureAwait(false));
        events.AddRange(await GetProjectDeadlineEventsAsync(userId, filter, cancellationToken).ConfigureAwait(false));
        events.AddRange(await GetGoalMilestoneEventsAsync(userId, filter, cancellationToken).ConfigureAwait(false));
        return events;
    }

    private async Task<List<CalendarFeedEventDto>> GetTaskEventsAsync(
        string userId,
        CalendarFilterDto? filter,
        CancellationToken cancellationToken)
    {
        var query = context.Tasks
            .AsNoTracking()
            .Where(t =>
                t.UserId == userId &&
                !t.IsArchived &&
                !t.Project.IsArchived &&
                t.Status != TaskItemStatus.Done &&
                t.Status != TaskItemStatus.Archived &&
                t.DueDate != null);

        if (filter is not null)
        {
            if (filter.ProjectId.HasValue)
                query = query.Where(t => t.ProjectId == filter.ProjectId.Value);

            if (filter.AreaId.HasValue)
                query = query.Where(t => t.Project.AreaId == filter.AreaId.Value);

            if (filter.Priority.HasValue)
                query = query.Where(t => t.Priority == filter.Priority.Value);

            if (filter.Status.HasValue)
                query = query.Where(t => t.Status == filter.Status.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(t => t.Title.ToLower().Contains(term));
            }
        }

        return await query
            .Select(t => new CalendarFeedEventDto(
                t.Id,
                CalendarFeedEventKind.Task,
                t.Title,
                t.DueDate!.Value,
                t.Project.Name,
                t.Project.Area != null ? t.Project.Area.Name : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<CalendarFeedEventDto>> GetProjectDeadlineEventsAsync(
        string userId,
        CalendarFilterDto? filter,
        CancellationToken cancellationToken)
    {
        var query = context.Projects
            .AsNoTracking()
            .Where(p =>
                p.UserId == userId &&
                !p.IsArchived &&
                p.Status != ProjectStatus.Completed &&
                p.Status != ProjectStatus.Archived &&
                p.DueDate != null);

        if (filter is not null)
        {
            if (filter.ProjectId.HasValue)
                query = query.Where(p => p.Id == filter.ProjectId.Value);

            if (filter.AreaId.HasValue)
                query = query.Where(p => p.AreaId == filter.AreaId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(term));
            }
        }

        return await query
            .Select(p => new CalendarFeedEventDto(
                p.Id,
                CalendarFeedEventKind.ProjectDeadline,
                p.Name,
                p.DueDate!.Value,
                p.Name,
                p.Area != null ? p.Area.Name : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<CalendarFeedEventDto>> GetGoalMilestoneEventsAsync(
        string userId,
        CalendarFilterDto? filter,
        CancellationToken cancellationToken)
    {
        var query = context.GoalMilestones
            .AsNoTracking()
            .Where(m =>
                m.Goal!.UserId == userId &&
                !m.Goal!.IsArchived &&
                !m.IsCompleted &&
                m.DueDate != null);

        if (filter is not null)
        {
            if (filter.AreaId.HasValue)
                query = query.Where(m => m.Goal!.AreaId == filter.AreaId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var term = filter.SearchTerm.Trim().ToLower();
                query = query.Where(m => m.Title.ToLower().Contains(term) || m.Goal!.Title.ToLower().Contains(term));
            }
        }

        return await query
            .Select(m => new CalendarFeedEventDto(
                m.Id,
                CalendarFeedEventKind.GoalMilestone,
                m.Title,
                m.DueDate!.Value,
                null,
                m.Goal!.Area != null ? m.Goal!.Area!.Name : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
