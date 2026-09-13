using System.Text;
using Brainy.Application.DTOs.Calendar;
using Brainy.Domain.Enums;

namespace Brainy.Application.Calendar;

/// <summary>
/// Renders <see cref="CalendarFeedEventDto"/> rows as an RFC 5545 <c>VCALENDAR</c> document
/// (issue #314). Kept as a small, pure, dependency-free formatter — no ASP.NET Core, no
/// database access — so it can be unit-tested directly against the RFC's grammar (line
/// folding, text escaping, all-day <c>DATE</c> values) without spinning up a web host.
/// </summary>
public static class IcsFeedWriter
{
    // RFC 2606 reserves ".invalid" as guaranteed non-resolvable, which is exactly what a UID
    // domain suffix needs to be here: globally-unique-looking without implying a live mailbox
    // or a real Brainy hostname (which could change independently of stored UIDs).
    private const string UidDomain = "brainy-app.invalid";
    private const int MaxLineOctets = 75;

    public static string Write(
        IReadOnlyCollection<CalendarFeedEventDto> events,
        DateTime generatedAtUtc,
        string calendarName = "Brainy Deadlines")
    {
        ArgumentNullException.ThrowIfNull(events);

        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:-//Brainy//Calendar Feed//EN");
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendLine(sb, "METHOD:PUBLISH");
        AppendLine(sb, $"X-WR-CALNAME:{Escape(calendarName)}");
        // Hints to clients that poll on their own schedule (Google, Apple) how often it is
        // worth re-fetching; ignored by ones that don't support it (notably classic Outlook).
        AppendLine(sb, "X-PUBLISHED-TTL:PT1H");
        AppendLine(sb, "REFRESH-INTERVAL;VALUE=DURATION:PT1H");

        foreach (var evt in events.OrderBy(e => e.Date).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            AppendEvent(sb, evt, generatedAtUtc);
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static void AppendEvent(StringBuilder sb, CalendarFeedEventDto evt, DateTime generatedAtUtc)
    {
        AppendLine(sb, "BEGIN:VEVENT");
        AppendLine(sb, $"UID:{KindSlug(evt.Kind)}-{evt.SourceId:D}@{UidDomain}");
        AppendLine(sb, $"DTSTAMP:{FormatUtcStamp(generatedAtUtc)}");

        // All-day event: the due date is already the user's calendar date (never a UTC
        // instant — see AGENTS.md), so it is written as a bare DATE value with no time
        // component and no time-zone conversion. DTEND is exclusive per RFC 5545 §3.6.1, so
        // a one-day event ends the following day.
        var date = evt.Date.Date;
        AppendLine(sb, $"DTSTART;VALUE=DATE:{FormatDate(date)}");
        AppendLine(sb, $"DTEND;VALUE=DATE:{FormatDate(date.AddDays(1))}");

        AppendLine(sb, $"SUMMARY:{Escape(Summarize(evt))}");

        var description = Describe(evt);
        if (description is not null)
            AppendLine(sb, $"DESCRIPTION:{Escape(description)}");

        AppendLine(sb, $"CATEGORIES:{Escape(CategoryName(evt.Kind))}");
        AppendLine(sb, "TRANSP:TRANSPARENT"); // an all-day deadline marker, not a busy block
        AppendLine(sb, "STATUS:CONFIRMED");
        AppendLine(sb, "END:VEVENT");
    }

    private static string Summarize(CalendarFeedEventDto evt) => evt.Kind switch
    {
        CalendarFeedEventKind.ProjectDeadline => $"{evt.Title} — Project deadline",
        CalendarFeedEventKind.GoalMilestone => $"{evt.Title} — Goal milestone",
        _ => evt.Title
    };

    private static string? Describe(CalendarFeedEventDto evt)
    {
        var parts = new List<string>();
        if (evt.Kind == CalendarFeedEventKind.Task && !string.IsNullOrWhiteSpace(evt.ProjectName))
            parts.Add($"Project: {evt.ProjectName}");
        if (!string.IsNullOrWhiteSpace(evt.AreaName))
            parts.Add($"Area: {evt.AreaName}");

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string CategoryName(CalendarFeedEventKind kind) => kind switch
    {
        CalendarFeedEventKind.Task => "Task",
        CalendarFeedEventKind.ProjectDeadline => "Project",
        CalendarFeedEventKind.GoalMilestone => "Goal Milestone",
        _ => "Task"
    };

    private static string KindSlug(CalendarFeedEventKind kind) => kind switch
    {
        CalendarFeedEventKind.Task => "task",
        CalendarFeedEventKind.ProjectDeadline => "project",
        CalendarFeedEventKind.GoalMilestone => "milestone",
        _ => "task"
    };

    private static string FormatDate(DateTime date) => date.ToString("yyyyMMdd");

    private static string FormatUtcStamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>Escapes text per RFC 5545 §3.3.11. Order matters: backslash must escape first.</summary>
    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");

    /// <summary>
    /// Writes one logical content line, folding it per RFC 5545 §3.1 so no physical line
    /// exceeds 75 octets: continuation lines start with a single space, which a parser strips
    /// while unfolding. Folding is octet-based (not char-based) so it never splits a
    /// multi-byte UTF-8 sequence — required here because Brainy titles are free-form user text.
    /// </summary>
    private static void AppendLine(StringBuilder sb, string content)
    {
        var remaining = content;
        var isFirstPhysicalLine = true;

        while (true)
        {
            var limit = isFirstPhysicalLine ? MaxLineOctets : MaxLineOctets - 1; // -1 for the leading fold space
            var (chunk, rest) = SplitAtOctetLimit(remaining, limit);

            if (!isFirstPhysicalLine)
                sb.Append(' ');
            sb.Append(chunk).Append("\r\n");

            if (rest.Length == 0)
                break;

            remaining = rest;
            isFirstPhysicalLine = false;
        }
    }

    private static (string Chunk, string Remainder) SplitAtOctetLimit(string value, int octetLimit)
    {
        if (Encoding.UTF8.GetByteCount(value) <= octetLimit)
            return (value, string.Empty);

        var octets = 0;
        var index = 0;
        while (index < value.Length)
        {
            var isSurrogatePair = char.IsHighSurrogate(value[index]) &&
                                   index + 1 < value.Length &&
                                   char.IsLowSurrogate(value[index + 1]);
            var codeUnitLength = isSurrogatePair ? 2 : 1;
            var codeUnitOctets = Encoding.UTF8.GetByteCount(value.AsSpan(index, codeUnitLength));

            if (octets + codeUnitOctets > octetLimit)
                break;

            octets += codeUnitOctets;
            index += codeUnitLength;
        }

        return (value[..index], value[index..]);
    }
}
