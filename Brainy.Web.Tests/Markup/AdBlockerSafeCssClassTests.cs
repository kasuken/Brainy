using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Brainy.Web.Tests.Markup;

/// <summary>
/// Ad blockers ship generic cosmetic filters (EasyList and friends) that force
/// <c>display: none</c> on elements whose class names look like advertising
/// containers, for example <c>ad-header</c>, <c>ad-section</c> or <c>banner</c>.
/// Such markup renders correctly in the DOM but is invisible for a large share
/// of real users, and it never reproduces on localhost because blockers usually
/// skip it. These tests keep our own class names out of that namespace.
/// </summary>
public class AdBlockerSafeCssClassTests
{
    private static readonly Regex ClassAttribute = new(
        "class\\s*=\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BlockedClassName = new(
        @"^(ad|ads|adv|advert|advertisement|banner|sponsor|sponsored|promo)([-_].*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void RazorMarkupDoesNotUseCssClassNamesThatAdBlockersHide()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateRazorFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match attribute in ClassAttribute.Matches(text))
            {
                var classNames = attribute.Groups["value"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(name => !name.Contains('@', StringComparison.Ordinal))
                    .Where(name => BlockedClassName.IsMatch(name));

                offenders.AddRange(classNames.Select(name =>
                    $"{Path.GetFileName(file)}: {name}"));
            }
        }

        offenders.Should().BeEmpty(
            "ad blockers hide these class names with display:none, which makes the markup invisible in production only");
    }

    private static IEnumerable<string> EnumerateRazorFiles()
    {
        var webProject = Path.Combine(FindRepositoryRoot(), "Brainy.Web");

        return Directory
            .EnumerateFiles(webProject, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
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
