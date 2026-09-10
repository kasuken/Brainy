using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>
/// The AI &amp; Your Data disclosure page (issue #297) is only useful if it is actually
/// reachable from a real AI-triggering surface in the authenticated app. These tests read
/// the raw Razor source (the same approach as <see cref="AdBlockerSafeCssClassTests"/>)
/// rather than rendering the component, since the AI generation dialogs run behind
/// authentication and MudBlazor dialog infrastructure that is out of scope here.
/// </summary>
public class AiTransparencyDiscoverabilityTests
{
    [Fact]
    public void OutputAiGenerationDialog_LinksToTheAiTransparencyPage()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "Brainy.Web", "Components", "Pages", "Outputs", "OutputAiGenerationDialog.razor");

        File.Exists(path).Should().BeTrue("the AI output-generation dialog is expected at this path");
        var markup = File.ReadAllText(path);

        markup.Should().Contain("href=\"/ai-transparency\"",
            "a user triggering AI generation must be able to find out what data leaves Brainy");
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
