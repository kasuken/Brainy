using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>
/// Issue #318 ("Extend global search into a command palette") extends
/// <c>GlobalSearchBar.razor</c> in place rather than adding a second overlay
/// component, keeps the existing record-search ARIA wiring intact, and adds a
/// shortcut reference to the existing <c>PageHelpWizardDialog</c>. These tests read
/// the raw Razor source — the same approach as <see cref="AdBlockerSafeCssClassTests"/>
/// — since exercising keyboard navigation end-to-end needs a live Blazor Server
/// circuit that this test project does not stand up.
/// </summary>
public sealed class CommandPaletteMarkupTests
{
    [Fact]
    public void GlobalSearchBar_RendersCommandsAsDistinctFromRecordResults()
    {
        var markup = ReadWebFile("Components", "Layout", "GlobalSearchBar.razor");

        // A single overlay ("do not build a second component") that still labels
        // commands with their own group heading and item class, distinguishing them
        // from the existing per-record-type groups.
        markup.Should().Contain("gsb__group--commands");
        markup.Should().Contain("gsb__item--command");
        markup.Should().Contain("<span class=\"gsb__group-label\">Commands</span>");
    }

    [Fact]
    public void GlobalSearchBar_CommandRowsAreKeyboardAccessibleOptions()
    {
        var markup = ReadWebFile("Components", "Layout", "GlobalSearchBar.razor");

        markup.Should().Contain("role=\"option\"");
        markup.Should().Contain("aria-selected=\"@IsCommandActive(localCommand.Id)\"");
        markup.Should().Contain("RunCommandAsync");
    }

    [Fact]
    public void GlobalSearchBar_PreservesRecordSearchAriaWiring()
    {
        var markup = ReadWebFile("Components", "Layout", "GlobalSearchBar.razor");

        // Unchanged plumbing from before issue #318: combobox + listbox pairing,
        // and the active-descendant hookup record rows still depend on.
        markup.Should().Contain("role=\"combobox\"");
        markup.Should().Contain("aria-haspopup=\"listbox\"");
        markup.Should().Contain("aria-activedescendant=\"@GetActiveDescendantId()\"");
        markup.Should().Contain("SearchService.SearchAsync(");
    }

    [Fact]
    public void PageHelpWizardDialog_ListsTheCommandPaletteShortcuts()
    {
        var markup = ReadWebFile("Components", "Help", "PageHelpWizardDialog.razor");

        markup.Should().Contain("Keyboard shortcuts");
        markup.Should().Contain("Ctrl");
        markup.Should().Contain("Open search &amp; commands");
    }

    [Theory]
    [InlineData("Notes.razor", "new")]
    [InlineData("Home.razor", "action")]
    public void PageWithPaletteQueryFlag_DeclaresTheExpectedSupplyParameterFromQuery(string fileNameUnused, string queryName)
    {
        // Home.razor lives directly under Components/Pages; Notes.razor too.
        var fileName = fileNameUnused;
        var markup = ReadWebFile("Components", "Pages", fileName);

        markup.Should().Contain($"[SupplyParameterFromQuery(Name = \"{queryName}\")]");
    }

    [Fact]
    public void ProjectsPage_ReopensItsOwnCreateDialogFromThePaletteQueryFlag()
    {
        var markup = ReadWebFile("Components", "Pages", "Projects", "ProjectsPage.razor");

        markup.Should().Contain("[SupplyParameterFromQuery(Name = \"new\")]");
        markup.Should().Contain("await OpenCreateDialog();");
    }

    [Fact]
    public void OutputsPage_ReopensItsOwnCreateDialogFromThePaletteQueryFlag()
    {
        var markup = ReadWebFile("Components", "Pages", "Outputs", "OutputsPage.razor");

        markup.Should().Contain("[SupplyParameterFromQuery(Name = \"new\")]");
        markup.Should().Contain("await OpenCreateDialogAsync();");
    }

    [Fact]
    public void HomePage_HandlesBothPaletteActionsWithoutTouchingAiCode()
    {
        var markup = ReadWebFile("Components", "Pages", "Home.razor");

        markup.Should().Contain("\"new-task\"");
        markup.Should().Contain("\"set-focus\"");
        markup.Should().NotContain("IAiAssistant");
        markup.Should().NotContain("AiAssistantOptions");
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
