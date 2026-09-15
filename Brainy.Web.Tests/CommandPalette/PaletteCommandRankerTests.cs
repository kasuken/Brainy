using AwesomeAssertions;
using Brainy.Web.Components.Layout.CommandPalette;
using Xunit;

namespace Brainy.Web.Tests.CommandPalette;

/// <summary>
/// Issue #318 requires "recent and frequent commands ranked first" and that the
/// palette's text matching not regress plain record search. <see cref="PaletteCommandRanker"/>
/// is plain C# (no Blazor/JS-interop dependency), so its filtering and ranking rules are
/// pinned here directly.
/// </summary>
public sealed class PaletteCommandRankerTests
{
    private static readonly PaletteCommand Today = Command("nav-today", "Go to Today", "home");
    private static readonly PaletteCommand Inbox = Command("nav-inbox", "Go to Inbox", "capture");
    private static readonly PaletteCommand NewTask = Command("create-task", "New task", "create");
    private static readonly PaletteCommand[] AllCommands = [Today, Inbox, NewTask];

    private static PaletteCommand Command(string id, string title, params string[] keywords) => new()
    {
        Id = id,
        Title = title,
        Kind = PaletteCommandKind.Navigation,
        Url = $"/{id}",
        Icon = "some-icon",
        Keywords = keywords,
    };

    [Fact]
    public void EmptyQuery_ReturnsEveryCommandUpToTheLimit()
    {
        var ranked = PaletteCommandRanker.Rank(AllCommands, query: null, usageCounts: null, recentCommandIds: null, limit: 2);

        ranked.Should().HaveCount(2);
    }

    [Fact]
    public void EmptyQuery_WithNoUsageData_OrdersAlphabeticallyByTitle()
    {
        var ranked = PaletteCommandRanker.Rank(AllCommands, query: "", usageCounts: null, recentCommandIds: null, limit: 10);

        ranked.Select(c => c.Title).Should().ContainInOrder("Go to Inbox", "Go to Today", "New task");
    }

    [Fact]
    public void Query_MatchesCommandTitleCaseInsensitively()
    {
        var ranked = PaletteCommandRanker.Rank(AllCommands, query: "TODAY", usageCounts: null, recentCommandIds: null, limit: 10);

        ranked.Should().ContainSingle().Which.Should().Be(Today);
    }

    [Fact]
    public void Query_MatchesCommandKeywordsAsWellAsTitle()
    {
        var ranked = PaletteCommandRanker.Rank(AllCommands, query: "capture", usageCounts: null, recentCommandIds: null, limit: 10);

        ranked.Should().ContainSingle().Which.Should().Be(Inbox);
    }

    [Fact]
    public void Query_WithNoMatch_ReturnsEmpty()
    {
        var ranked = PaletteCommandRanker.Rank(AllCommands, query: "nonexistent-command", usageCounts: null, recentCommandIds: null, limit: 10);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public void RecentlyRunCommands_RankBeforeCommandsNeverRun()
    {
        var recent = new List<string> { NewTask.Id };

        var ranked = PaletteCommandRanker.Rank(AllCommands, query: null, usageCounts: null, recentCommandIds: recent, limit: 10);

        ranked.First().Should().Be(NewTask, "a recently-run command must rank first even though it is last alphabetically");
    }

    [Fact]
    public void AmongNonRecentCommands_HigherUsageCountRanksFirst()
    {
        var usage = new Dictionary<string, int>
        {
            [Today.Id] = 1,
            [Inbox.Id] = 5,
        };

        var ranked = PaletteCommandRanker.Rank(AllCommands, query: null, usageCounts: usage, recentCommandIds: null, limit: 10);

        ranked.Select(c => c.Id).Should().ContainInOrder(Inbox.Id, Today.Id, NewTask.Id);
    }

    [Fact]
    public void RecentRanking_TakesPriorityOverFrequency()
    {
        var usage = new Dictionary<string, int> { [Inbox.Id] = 50 };
        var recent = new List<string> { NewTask.Id };

        var ranked = PaletteCommandRanker.Rank(AllCommands, query: null, usageCounts: usage, recentCommandIds: recent, limit: 10);

        ranked.First().Should().Be(NewTask, "recency must outrank frequency, not the other way round");
    }

    [Fact]
    public void Limit_CapsTheResultCountEvenWhenMoreCommandsMatch()
    {
        var ranked = PaletteCommandRanker.Rank(PaletteCommandCatalog.All, query: null, usageCounts: null, recentCommandIds: null, limit: 3);

        ranked.Should().HaveCount(3);
    }
}
