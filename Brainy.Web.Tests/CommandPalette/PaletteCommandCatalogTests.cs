using AwesomeAssertions;
using Brainy.Web.Components.Layout.CommandPalette;
using Xunit;

namespace Brainy.Web.Tests.CommandPalette;

/// <summary>
/// Issue #318 ("Extend global search into a command palette") requires navigation
/// commands for every top-level surface (including the just-added <c>/tags</c> page),
/// creation shortcuts for note/task/project/idea/output, and the three named
/// contextual actions. These tests pin the catalog to that contract so a future edit
/// cannot silently drop one.
/// </summary>
public sealed class PaletteCommandCatalogTests
{
    [Fact]
    public void AllCommands_HaveUniqueNonEmptyIds()
    {
        var ids = PaletteCommandCatalog.All.Select(c => c.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public void AllCommands_HaveATitleAndAnAppRelativeUrl()
    {
        foreach (var command in PaletteCommandCatalog.All)
        {
            command.Title.Should().NotBeNullOrWhiteSpace();
            command.Icon.Should().NotBeNullOrWhiteSpace();
            command.Url.Should().StartWith("/", $"command '{command.Id}' must resolve to an in-app route");
        }
    }

    [Theory]
    [InlineData("/today")]
    [InlineData("/inbox")]
    [InlineData("/projects")]
    [InlineData("/tasks-hub")]
    [InlineData("/tasks-calendar")]
    [InlineData("/notes")]
    [InlineData("/search")]
    [InlineData("/para")]
    [InlineData("/areas")]
    [InlineData("/resources")]
    [InlineData("/goals")]
    [InlineData("/ideas")]
    [InlineData("/tags")]
    [InlineData("/archives")]
    [InlineData("/pulse")]
    [InlineData("/outputs")]
    public void NavigationCommands_CoverEveryTopLevelSurface(string route)
    {
        PaletteCommandCatalog.All
            .Should().ContainSingle(c => c.Kind == PaletteCommandKind.Navigation && c.Url == route,
                $"every top-level surface, including the recently-added {route}, must be reachable from the palette");
    }

    [Fact]
    public void NavigationCommands_IncludeTagsPage()
    {
        // Called out explicitly in issue #318: "/tags" was just added and must be included.
        PaletteCommandCatalog.All
            .Should().ContainSingle(c => c.Kind == PaletteCommandKind.Navigation && c.Url == "/tags");
    }

    [Theory]
    [InlineData("/notes?new=true")]
    [InlineData("/today?action=new-task")]
    [InlineData("/projects?new=true")]
    [InlineData("/ideas/new")]
    [InlineData("/outputs?new=true")]
    public void CreateCommands_CoverEveryRecordType(string url)
    {
        PaletteCommandCatalog.All
            .Should().ContainSingle(c => c.Kind == PaletteCommandKind.Create && c.Url == url);
    }

    [Fact]
    public void ContextualActionCommands_MatchTheThreeNamedInTheIssue()
    {
        var actions = PaletteCommandCatalog.All
            .Where(c => c.Kind == PaletteCommandKind.Action)
            .ToList();

        actions.Should().HaveCount(3);
        actions.Should().ContainSingle(c => c.Title == "Set current focus" && c.Url == "/today?action=set-focus");
        actions.Should().ContainSingle(c => c.Title == "Start weekly review" && c.Url == "/today/week/review");
        actions.Should().ContainSingle(c => c.Title == "Process Inbox" && c.Url == "/inbox");
    }

    [Fact]
    public void Catalog_NeverReferencesAiFeatures()
    {
        // AI (IAiAssistant, BYOK, metering) is explicitly out of scope for issue #318.
        foreach (var command in PaletteCommandCatalog.All)
        {
            command.Title.Should().NotContain("AI");
            command.Id.Should().NotContain("ai-");
        }
    }
}
