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
    [InlineData(AppTheme.MidnightBlush)]
    [InlineData(AppTheme.OneDarkPro)]
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

    [Fact]
    public void GetTheme_ForMidnightBlush_UsesMidnightBlushDarkPalette()
    {
        var theme = AppThemes.GetTheme(AppTheme.MidnightBlush);

        theme.PaletteDark.Primary.ToString().Should().Be(AppThemes.MidnightBlushTheme.PaletteDark.Primary.ToString());
    }

    [Fact]
    public void MidnightBlushTheme_UsesMidnightBlushPrimaryAccentColor()
    {
        var theme = AppThemes.MidnightBlushTheme;

        // #db2777 (Midnight Blush rose) as rgba.
        theme.PaletteDark.Primary.ToString().Should().Be("rgba(219,39,119,1)");
    }

    [Fact]
    public void GetTheme_ForOneDarkPro_UsesOneDarkProDarkPalette()
    {
        var theme = AppThemes.GetTheme(AppTheme.OneDarkPro);

        theme.PaletteDark.Primary.ToString().Should().Be(AppThemes.OneDarkProTheme.PaletteDark.Primary.ToString());
    }

    [Fact]
    public void OneDarkProTheme_UsesOneDarkProPrimaryAccentColor()
    {
        var theme = AppThemes.OneDarkProTheme;

        // #61afef (One Dark Pro Malibu blue) as rgba.
        theme.PaletteDark.Primary.ToString().Should().Be("rgba(97,175,239,1)");
    }
}
