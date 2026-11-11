using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

public sealed class SupportContactMarkupTests
{
    [Fact]
    public void AccountManage_ProvidesSupportEmailAndBugReportingLink()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Brainy.Web",
            "Components",
            "Account",
            "Pages",
            "Manage.razor"));

        markup.Should().Contain("Help and support");
        markup.Should().Contain("href=\"mailto:support@brainy-me.com\"");
        markup.Should().Contain("href=\"https://github.com/kasuken/Brainy/issues\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brainy.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
