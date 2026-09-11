using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>
/// The Resume Context panel (issue #305) sits inside the authenticated Today experience
/// behind full Blazor Server interactivity, which is out of reach for an HttpClient-based
/// test (see <see cref="ProductionSurface.ProductionSurfaceTests"/> for what that boundary
/// actually covers). Following the same approach as <see cref="AiTransparencyDiscoverabilityTests"/>,
/// these tests read the raw Razor source to verify the panel is actually wired into the
/// current-focus card and that its guardrails are present in markup, not just in a service
/// that could silently go unused.
/// </summary>
public class ResumeContextPanelMarkupTests
{
    private static string RepositoryRoot => FindRepositoryRoot();

    private static string ReadRazor(params string[] relativeSegments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, "Brainy.Web", "Components", "Pages", .. relativeSegments]));

    [Fact]
    public void CurrentTaskWidget_EmbedsTheResumeContextPanel()
    {
        var markup = ReadRazor("Today", "CurrentTaskWidget.razor");

        markup.Should().Contain("<ResumeContextPanel",
            "the Resume Context panel must be reachable from the current-focus card, not a separate dashboard page");
    }

    [Fact]
    public void ResumeContextPanel_CommunicatesArchivedWaitingAndBlockedStateDistinctly()
    {
        var markup = ReadRazor("Today", "ResumeContextPanel.razor");

        markup.Should().Contain("rc-badge--archived");
        markup.Should().Contain("rc-badge--waiting");
        markup.Should().Contain("rc-badge--blocked");
        markup.Should().Contain("ctx.IsArchived");
        markup.Should().Contain("TaskItemStatus.Waiting");
        markup.Should().Contain("ctx.IsBlocked");
    }

    [Fact]
    public void ResumeContextPanel_SupportsAddingAndEditingARestartNote()
    {
        var markup = ReadRazor("Today", "ResumeContextPanel.razor");

        markup.Should().Contain("SaveRestartNoteAsync",
            "acceptance criteria: a user can add or edit a restart note from the focus surface");
        markup.Should().Contain("_noteDraft");
    }

    [Fact]
    public void ResumeContextPanel_IsCollapsible()
    {
        var markup = ReadRazor("Today", "ResumeContextPanel.razor");

        markup.Should().Contain("_collapsed",
            "the panel must be compact and user-dismissible, not a permanent dashboard fixture");
    }

    [Fact]
    public void ResumeContextPanel_NeverCallsAnAiService()
    {
        var markup = ReadRazor("Today", "ResumeContextPanel.razor");

        // Guardrail: "do not infer a next action from AI without labelling it as a suggestion
        // and asking for review". The safest reading, per issue #305, is no AI call at all —
        // every field here is derived from subtasks/dependencies/project data.
        markup.Should().NotContain("IAiAssistant");
        markup.Should().NotContain("AiAssistant", "no AI assistant dependency of any kind");
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
