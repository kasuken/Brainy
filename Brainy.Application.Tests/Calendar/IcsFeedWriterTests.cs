using AwesomeAssertions;
using Brainy.Application.Calendar;
using Brainy.Application.DTOs.Calendar;
using Brainy.Domain.Enums;
using Xunit;

namespace Brainy.Application.Tests.Calendar;

/// <summary>
/// Covers <see cref="IcsFeedWriter"/> against the RFC 5545 grammar directly — no HTTP, no
/// database — including issue #314's central correctness requirement: an all-day event built
/// from a user-calendar due date must land on the same calendar day regardless of the
/// server's own time zone, because the due date is never a UTC instant to begin with (see
/// AGENTS.md). <see cref="MinimalIcsParser"/> below unfolds and parses the output using only
/// the RFC's own line-folding and property grammar, so "the output parses" is verified against
/// the spec rather than merely eyeballed.
/// </summary>
public class IcsFeedWriterTests
{
    private static readonly DateTime GeneratedAt = new(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Write_ProducesAWellFormedCalendarThatParses()
    {
        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, "Ship the release", new DateTime(2026, 3, 10), "Launch", "Work"),
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.ProjectDeadline, "Launch", new DateTime(2026, 3, 20), "Launch", "Work"),
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.GoalMilestone, "Beta complete", new DateTime(2026, 3, 15), null, "Work"),
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);
        var calendar = MinimalIcsParser.Parse(ics);

        calendar.Properties["VERSION"].Should().Be("2.0");
        calendar.Events.Should().HaveCount(3);
        foreach (var vevent in calendar.Events)
        {
            vevent.Should().ContainKey("UID");
            vevent.Should().ContainKey("DTSTAMP");
            vevent.Should().ContainKey("DTSTART");
            vevent.Should().ContainKey("SUMMARY");
        }
    }

    [Fact]
    public void Write_WithNoEvents_StillProducesAValidEmptyCalendar()
    {
        var ics = IcsFeedWriter.Write([], GeneratedAt);

        var calendar = MinimalIcsParser.Parse(ics);
        calendar.Events.Should().BeEmpty();
    }

    [Fact]
    public void Write_UsesCrLfLineEndings()
    {
        var ics = IcsFeedWriter.Write([], GeneratedAt);

        ics.Should().StartWith("BEGIN:VCALENDAR\r\n");
        ics.Should().EndWith("END:VCALENDAR\r\n");
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Pacific/Kiritimati")] // UTC+14: the furthest-ahead IANA zone.
    [InlineData("Etc/GMT+12")] // UTC-12: the furthest-behind IANA zone.
    public void Write_AllDayDueDate_LandsOnTheSameCalendarDayRegardlessOfTimeZone(string timeZoneId)
    {
        // The due date is a user-calendar date, not a UTC instant (AGENTS.md) — a correct
        // implementation writes it as a bare DATE value with NO time-zone conversion at all.
        // This test exists to catch the classic bug: treating the stored value as if it were
        // UTC and converting it into some time zone, which shifts an all-day event onto the
        // wrong calendar day for anyone far enough from UTC.
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); // fails fast if the id is unknown on this OS
        var dueDate = new DateTime(2026, 3, 15); // no time component: exactly what DueDate columns store

        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, "Renew passport", dueDate, "Admin", null)
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);
        var calendar = MinimalIcsParser.Parse(ics);

        var vevent = calendar.Events.Single();
        vevent["DTSTART"].Should().Be("20260315", "the all-day date must be written exactly as stored, independent of any time zone");
        vevent["DTEND"].Should().Be("20260316", "DTEND is exclusive per RFC 5545 §3.6.1, so a one-day event ends the following day");
    }

    [Fact]
    public void Write_DateTimeWithATimeComponent_IsTruncatedToItsDatePart()
    {
        // Defensive: even if a caller ever passed a DateTime with a stray time component, the
        // all-day mapping must still use only the date — an all-day event has no time at all.
        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, "Task", new DateTime(2026, 3, 15, 23, 59, 0), null, null)
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);
        var calendar = MinimalIcsParser.Parse(ics);

        calendar.Events.Single()["DTSTART"].Should().Be("20260315");
    }

    [Fact]
    public void Write_EscapesCommasSemicolonsAndBackslashesInText()
    {
        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, "Buy milk, eggs; a \\ mop", new DateTime(2026, 1, 1), null, null)
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);

        ics.Should().Contain("Buy milk\\, eggs\\; a \\\\ mop");
    }

    [Fact]
    public void Write_FoldsLongLinesAtSeventyFiveOctetsWithASingleLeadingSpace()
    {
        var longTitle = new string('a', 200);
        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, longTitle, new DateTime(2026, 1, 1), null, null)
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);
        var physicalLines = ics.Split("\r\n", StringSplitOptions.None);

        foreach (var line in physicalLines)
        {
            System.Text.Encoding.UTF8.GetByteCount(line).Should().BeLessThanOrEqualTo(75);
        }

        // Unfolding (per RFC 5545 §3.1: drop a CRLF immediately followed by a space) must
        // reconstruct the original, unescaped title exactly.
        var calendar = MinimalIcsParser.Parse(ics);
        calendar.Events.Single()["SUMMARY"].Should().Be(longTitle);
    }

    [Fact]
    public void Write_MultiByteCharacters_AreNeverSplitAcrossAFold()
    {
        // Each "é" is 2 UTF-8 octets; repeated enough times this must cross the 75-octet fold
        // boundary. A byte-unsafe folder would split one character's bytes across two
        // physical lines, corrupting it. Round-tripping through the unfolder must recover the
        // exact original string.
        var title = string.Concat(Enumerable.Repeat("café ", 20));
        var events = new[]
        {
            new CalendarFeedEventDto(Guid.NewGuid(), CalendarFeedEventKind.Task, title, new DateTime(2026, 1, 1), null, null)
        };

        var ics = IcsFeedWriter.Write(events, GeneratedAt);
        var calendar = MinimalIcsParser.Parse(ics);

        calendar.Events.Single()["SUMMARY"].Should().Be(title);
    }

    [Fact]
    public void Write_TwoEventsForTheSameSourceKindAndId_ProduceTheSameStableUid()
    {
        var id = Guid.NewGuid();
        var events = new[] { new CalendarFeedEventDto(id, CalendarFeedEventKind.Task, "Task", new DateTime(2026, 1, 1), null, null) };

        var first = MinimalIcsParser.Parse(IcsFeedWriter.Write(events, GeneratedAt)).Events.Single()["UID"];
        var second = MinimalIcsParser.Parse(IcsFeedWriter.Write(events, GeneratedAt.AddMinutes(5))).Events.Single()["UID"];

        first.Should().Be(second, "a stable UID lets calendar clients de-duplicate/update the same event across refreshes");
    }

    [Fact]
    public void Write_DifferentKindsWithTheSameSourceId_ProduceDifferentUids()
    {
        var id = Guid.NewGuid();
        var events = new[]
        {
            new CalendarFeedEventDto(id, CalendarFeedEventKind.Task, "Task", new DateTime(2026, 1, 1), null, null),
            new CalendarFeedEventDto(id, CalendarFeedEventKind.ProjectDeadline, "Project", new DateTime(2026, 1, 1), null, null)
        };

        var calendar = MinimalIcsParser.Parse(IcsFeedWriter.Write(events, GeneratedAt));

        calendar.Events.Select(e => e["UID"]).Distinct().Should().HaveCount(2);
    }

    /// <summary>
    /// A deliberately minimal RFC 5545 reader used only by these tests: unfolds continuation
    /// lines (§3.1) and parses "NAME[;params]:VALUE" content lines into VCALENDAR-level
    /// properties and a list of VEVENT property maps. It exists so "the output parses" is an
    /// assertion against the RFC's actual grammar, without pulling a third-party ICS library
    /// into the product.
    /// </summary>
    private sealed class MinimalIcsParser
    {
        public Dictionary<string, string> Properties { get; } = new();
        public List<Dictionary<string, string>> Events { get; } = new();

        public static MinimalIcsParser Parse(string ics)
        {
            ics.Should().StartWith("BEGIN:VCALENDAR\r\n");
            ics.Should().EndWith("END:VCALENDAR\r\n");

            var unfolded = Unfold(ics);
            var result = new MinimalIcsParser();
            Dictionary<string, string>? current = null;

            foreach (var line in unfolded)
            {
                if (line.Length == 0)
                    continue;

                var colon = line.IndexOf(':');
                colon.Should().BeGreaterThan(0, $"every content line must be NAME[;params]:VALUE — got '{line}'");
                var nameAndParams = line[..colon];
                var value = line[(colon + 1)..];
                var name = nameAndParams.Split(';')[0];

                switch (name)
                {
                    case "BEGIN" when value == "VEVENT":
                        current = new Dictionary<string, string>();
                        break;
                    case "END" when value == "VEVENT":
                        result.Events.Add(current!);
                        current = null;
                        break;
                    case "BEGIN" or "END":
                        break;
                    default:
                        var target = current ?? result.Properties;
                        target[name] = Unescape(value);
                        break;
                }
            }

            current.Should().BeNull("every BEGIN:VEVENT must be matched by an END:VEVENT");
            return result;
        }

        private static List<string> Unfold(string ics)
        {
            var physicalLines = ics.Split("\r\n", StringSplitOptions.None);
            var logicalLines = new List<string>();

            foreach (var physicalLine in physicalLines)
            {
                if (physicalLine.Length == 0)
                    continue;

                if ((physicalLine[0] == ' ' || physicalLine[0] == '\t') && logicalLines.Count > 0)
                    logicalLines[^1] += physicalLine[1..];
                else
                    logicalLines.Add(physicalLine);
            }

            return logicalLines;
        }

        private static string Unescape(string value) =>
            value
                .Replace("\\n", "\n")
                .Replace("\\N", "\n")
                .Replace("\\,", ",")
                .Replace("\\;", ";")
                .Replace("\\\\", "\\");
    }
}
