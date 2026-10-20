using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>
/// Verifies the layout, card metrics, standards callouts, accessibility, and view modes
/// on the Areas homepage (<c>AreasPage.razor</c>).
/// </summary>
public sealed class AreasPageMarkupTests
{
    [Fact]
    public void AreasPage_UsesAreaSummaryService_AndRendersLiveMetrics()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        // Uses the enriched summary endpoints
        markup.Should().Contain("AreaService.GetActiveSummariesAsync()");
        markup.Should().Contain("AreaService.GetArchivedSummariesAsync()");

        // Metric tiles in cards
        markup.Should().Contain("ap-card-metrics");
        markup.Should().Contain("ActiveProjectCount");
        markup.Should().Contain("OpenTaskCount");
        markup.Should().Contain("OverdueTaskCount");
        markup.Should().Contain("NoteCount");
    }

    [Fact]
    public void AreasPage_RendersPurposeCallout_ForParaStandards()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        markup.Should().Contain("ap-card-purpose");
        markup.Should().Contain("Standard &amp; Purpose");
        markup.Should().Contain("a.Purpose");
    }

    [Fact]
    public void AreasPage_RendersOverduePill_AndHealthBadges()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        markup.Should().Contain("ap-badge--attention");
        markup.Should().Contain("ap-card-ribbon--attention");
        markup.Should().Contain("ap-metric-tile--overdue");
        markup.Should().Contain("overdue");
    }

    [Fact]
    public void AreasPage_RendersExecutiveOverviewRibbon_AndQuickFilters()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        // Overview strip
        markup.Should().Contain("ap-overview-strip");
        markup.Should().Contain("Active Areas");
        markup.Should().Contain("Active Projects");
        markup.Should().Contain("Open Tasks");
        markup.Should().Contain("Notes &amp; Ideas");

        // Quick filter chips
        markup.Should().Contain("With Projects");
        markup.Should().Contain("Needs Attention");
        markup.Should().Contain("With Notes");
    }

    [Fact]
    public void AreasPage_ProvidesViewSwitcher_WithCardAndTableModes()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        // View switcher controls
        markup.Should().Contain("ap-view-switch");
        markup.Should().Contain("_tableViewEnabled");
        markup.Should().Contain("<MudTable");
        markup.Should().Contain("Purpose &amp; Standards");
    }

    [Fact]
    public void AreasPage_PreservesAriaAccessibility_AndHelpGuide()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        markup.Should().Contain("aria-label=\"Search areas\"");
        markup.Should().Contain("aria-pressed");
        markup.Should().Contain("<PageHelpButton Guide=\"HelpGuide\" />");
        markup.Should().Contain("<UserLocalTime Utc=\"a.UpdatedAtUtc\" />");
    }

    [Fact]
    public void AreasPage_AreaCardTitles_AreFullyVisibleAndNotClippedByNowrap()
    {
        var markup = ReadWebFile("Components", "Pages", "Areas", "AreasPage.razor");

        // The card title heading and link
        markup.Should().Contain("ap-card-title");
        markup.Should().Contain("ap-card-link");
        markup.Should().Contain("title=\"@a.Name\"");

        // Card titles allow word wrapping up to 2 lines and are not artificially cut off by white-space: nowrap
        markup.Should().Contain("word-break: break-word");
        markup.Should().Contain("-webkit-line-clamp: 2");

        // Subbar separates badges and actions from header to give titles full card width
        markup.Should().Contain("ap-card-subbar");
        markup.Should().Contain("ap-card-badges");
        markup.Should().Contain("ap-card-actions");
    }

    private static string ReadWebFile(params string[] relativeSegments)
    {
        var path = Path.Combine([FindRepositoryRoot(), "Brainy.Web", .. relativeSegments]);
        File.Exists(path).Should().BeTrue($"expected file at {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("Brainy.slnx").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
