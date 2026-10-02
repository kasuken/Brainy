using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>Verifies that Today surfaces preserve actionable task status validation messages.</summary>
public sealed class TodayTaskStatusErrorMarkupTests
{
    [Theory]
    [InlineData("Components/Pages/Home.razor")]
    [InlineData("Components/Pages/Today/CurrentTaskWidget.razor")]
    public void SetInProgressHandler_ShowsExpectedValidationReason(string relativePath)
    {
        var markup = ReadWebFile(relativePath);

        markup.Should().Contain("catch (InvalidOperationException ex)");
        markup.Should().Contain("Snackbar.Add(ex.Message, Severity.Error);");
    }

    private static string ReadWebFile(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), "Brainy.Web", relativePath);
        File.Exists(path).Should().BeTrue($"expected file at {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("Brainy.slnx").Any())
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
