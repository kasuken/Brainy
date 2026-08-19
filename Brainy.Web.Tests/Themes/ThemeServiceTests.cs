using AwesomeAssertions;
using Brainy.Web.Themes;
using Xunit;

namespace Brainy.Web.Tests.Themes;

public class ThemeServiceTests
{
    [Fact]
    public void Theme_DefaultsToBrainy()
    {
        var service = new ThemeService();

        service.Theme.Should().Be(AppTheme.Brainy);
    }

    [Fact]
    public void Theme_WhenSetToNewValue_RaisesOnThemeChanged()
    {
        var service = new ThemeService();
        var raised = false;
        service.OnThemeChanged += () => raised = true;

        service.Theme = AppTheme.Dracula;

        raised.Should().BeTrue();
        service.Theme.Should().Be(AppTheme.Dracula);
    }

    [Fact]
    public void Theme_WhenSetToSameValue_DoesNotRaiseOnThemeChanged()
    {
        var service = new ThemeService { Theme = AppTheme.Minimal };
        var raiseCount = 0;
        service.OnThemeChanged += () => raiseCount++;

        service.Theme = AppTheme.Minimal;

        raiseCount.Should().Be(0);
    }
}
