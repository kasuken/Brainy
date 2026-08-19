using AwesomeAssertions;
using Brainy.Web.Themes;
using Xunit;

namespace Brainy.Web.Tests.Themes;

public class AppThemesTests
{
    [Theory]
    [InlineData(AppTheme.Brainy)]
    [InlineData(AppTheme.Minimal)]
    [InlineData(AppTheme.Dracula)]
    public void GetTheme_ReturnsNonNullThemeForEveryAppTheme(AppTheme theme)
    {
        var result = AppThemes.GetTheme(theme);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetTheme_ForBrainy_UsesBrainyLightPalette()
    {
        var theme = AppThemes.GetTheme(AppTheme.Brainy);

        theme.PaletteLight.Primary.ToString().Should().Be(AppThemes.BrainyTheme.PaletteLight.Primary.ToString());
    }

    [Fact]
    public void GetTheme_ForMinimal_UsesMinimalLightPalette()
    {
        var theme = AppThemes.GetTheme(AppTheme.Minimal);

        theme.PaletteLight.Primary.ToString().Should().Be(AppThemes.MinimalTheme.PaletteLight.Primary.ToString());
    }

    [Fact]
    public void GetTheme_ForDracula_UsesDraculaDarkPalette()
    {
        var theme = AppThemes.GetTheme(AppTheme.Dracula);

        theme.PaletteDark.Primary.ToString().Should().Be(AppThemes.DraculaTheme.PaletteDark.Primary.ToString());
    }

    [Fact]
    public void DraculaTheme_UsesDraculaPrimaryAccentColor()
    {
        var theme = AppThemes.DraculaTheme;

        // #bd93f9 (Dracula purple) as rgba.
        theme.PaletteDark.Primary.ToString().Should().Be("rgba(189,147,249,1)");
    }
}
