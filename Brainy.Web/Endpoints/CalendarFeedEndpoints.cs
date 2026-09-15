using Brainy.Application.Calendar;
using Brainy.Application.DTOs.Calendar;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;

namespace Brainy.Web.Endpoints;

/// <summary>
/// Public, anonymous ICS calendar feed (issue #314) for Outlook/Google/Apple-style calendar
/// subscriptions. Deliberately <b>not</b> <c>.RequireAuthorization()</c> like
/// <see cref="NoteImageEndpoints"/> or <see cref="MarkdownExportEndpoints"/>: calendar
/// clients poll a bare URL and cannot perform an interactive login, so the feed token itself
/// (in the <c>token</c> query parameter — never a path segment, so it stays covered by
/// <c>PrivacyRedactionProcessor</c>'s existing <c>url.query</c> redaction) is the entire
/// credential. <see cref="ICalendarFeedTokenService.ResolveUserIdAsync"/> is the only thing
/// standing between "public URL" and "this user's private deadlines" — everything downstream
/// only ever sees the resolved user id, never the token.
/// </summary>
public static class CalendarFeedEndpoints
{
    public const string FeedPath = "/api/calendar/feed.ics";

    public static IEndpointRouteBuilder MapCalendarFeedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(FeedPath, async (
            HttpContext http,
            ICalendarFeedTokenService tokenService,
            ICalendarFeedService feedService,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var query = http.Request.Query;
            var token = query["token"].ToString();

            var userId = await tokenService.ResolveUserIdAsync(token, cancellationToken).ConfigureAwait(false);
            if (userId is null)
            {
                // 404, not 401/403: an unauthenticated caller should not learn "this token
                // exists but is wrong" versus "no such endpoint" — same response either way.
                return Results.NotFound();
            }

            var filter = BuildFilter(query);
            var events = await feedService.GetFeedEventsAsync(userId, filter, cancellationToken).ConfigureAwait(false);
            var ics = IcsFeedWriter.Write(events, timeProvider.GetUtcNow().UtcDateTime);

            return Results.Text(ics, "text/calendar; charset=utf-8");
        });

        return endpoints;
    }

    private static CalendarFilterDto BuildFilter(IQueryCollection query)
    {
        Guid? projectId = Guid.TryParse(query["projectId"], out var project) ? project : null;
        Guid? areaId = Guid.TryParse(query["areaId"], out var area) ? area : null;
        TaskPriority? priority = Enum.TryParse<TaskPriority>(query["priority"], ignoreCase: true, out var p) ? p : null;
        TaskItemStatus? status = Enum.TryParse<TaskItemStatus>(query["status"], ignoreCase: true, out var s) ? s : null;
        var searchTerm = query["q"].ToString();

        return new CalendarFilterDto(
            projectId,
            areaId,
            priority,
            status,
            string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm);
    }
}
