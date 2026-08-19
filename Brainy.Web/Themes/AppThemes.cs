using MudBlazor;

namespace Brainy.Web.Themes;

public static class AppThemes
{
    public static MudTheme BrainyTheme => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#c0561d",
            PrimaryContrastText = "#fdf8f0",
            Secondary = "#6f7a5a",
            Tertiary = "#cd9a3a",
            Background = "#f3ede1",
            BackgroundGray = "#ece3d3",
            Surface = "#fcfaf4",
            AppbarBackground = "#1d1812",
            AppbarText = "#f4ece0",
            DrawerBackground = "#fbf7ef",
            DrawerText = "#3a342b",
            DrawerIcon = "#7a7163",
            TextPrimary = "#211d17",
            TextSecondary = "#6b6253",
            ActionDefault = "#6b6253",
            Divider = "#ded5c4",
            LinesDefault = "#ded5c4",
            LinesInputs = "#cabea8",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = new[] { "Hanken Grotesk", "sans-serif" } },
            H1 = new H1Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "500" },
            H2 = new H2Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "500" },
            H3 = new H3Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "500" },
            H4 = new H4Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "500" },
            H5 = new H5Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "600" },
            H6 = new H6Typography { FontFamily = new[] { "Fraunces", "serif" }, FontWeight = "600" },
            Subtitle1 = new Subtitle1Typography { FontFamily = new[] { "Hanken Grotesk", "sans-serif" } },
            Subtitle2 = new Subtitle2Typography { FontFamily = new[] { "Hanken Grotesk", "sans-serif" } },
            Button = new ButtonTypography { FontFamily = new[] { "Hanken Grotesk", "sans-serif" }, FontWeight = "600", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
        }
    };

    /// <summary>
    /// Notion-inspired theme: flat white workspace, warm graphite ink,
    /// Notion-blue accent, and the system UI font stack.
    /// </summary>
    public static MudTheme MinimalTheme => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2383e2",
            PrimaryContrastText = "#ffffff",
            Secondary = "#448361",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#cb912f",
            Background = "#ffffff",
            BackgroundGray = "#f7f7f5",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#37352f",
            DrawerBackground = "#f7f7f5",
            DrawerText = "#37352f",
            DrawerIcon = "#787774",
            TextPrimary = "#37352f",
            TextSecondary = "#787774",
            TextDisabled = "#b9b9b7",
            ActionDefault = "#787774",
            Divider = "#ededec",
            LinesDefault = "#ededec",
            LinesInputs = "#e0e0de",
            TableLines = "#ededec",
            HoverOpacity = 0.04,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = MinimalFontStack },
            H1 = new H1Typography { FontFamily = MinimalFontStack, FontWeight = "700" },
            H2 = new H2Typography { FontFamily = MinimalFontStack, FontWeight = "700" },
            H3 = new H3Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H4 = new H4Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H5 = new H5Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H6 = new H6Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            Subtitle1 = new Subtitle1Typography { FontFamily = MinimalFontStack },
            Subtitle2 = new Subtitle2Typography { FontFamily = MinimalFontStack },
            Button = new ButtonTypography { FontFamily = MinimalFontStack, FontWeight = "500", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
        }
    };

    /// <summary>
    /// Dracula theme: the popular dark palette from https://draculatheme.com,
    /// as shipped in the official Dracula Visual Studio Code theme. Rendered
    /// via MudTheme.PaletteDark together with MudThemeProvider.IsDarkMode.
    /// </summary>
    public static MudTheme DraculaTheme => new MudTheme
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#bd93f9",
            PrimaryContrastText = "#191a21",
            Secondary = "#ff79c6",
            SecondaryContrastText = "#191a21",
            Tertiary = "#8be9fd",
            TertiaryContrastText = "#191a21",
            Success = "#50fa7b",
            SuccessContrastText = "#191a21",
            Info = "#8be9fd",
            InfoContrastText = "#191a21",
            Warning = "#ffb86c",
            WarningContrastText = "#191a21",
            Error = "#ff5555",
            ErrorContrastText = "#191a21",
            Background = "#282a36",
            BackgroundGray = "#21222c",
            Surface = "#343746",
            AppbarBackground = "#191a21",
            AppbarText = "#f8f8f2",
            DrawerBackground = "#21222c",
            DrawerText = "#f8f8f2",
            DrawerIcon = "#6272a4",
            TextPrimary = "#f8f8f2",
            TextSecondary = "#6272a4",
            ActionDefault = "#6272a4",
            Divider = "#44475a",
            LinesDefault = "#44475a",
            LinesInputs = "#424450",
            TableLines = "#44475a",
            HoverOpacity = 0.06,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = MinimalFontStack },
            H1 = new H1Typography { FontFamily = MinimalFontStack, FontWeight = "700" },
            H2 = new H2Typography { FontFamily = MinimalFontStack, FontWeight = "700" },
            H3 = new H3Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H4 = new H4Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H5 = new H5Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            H6 = new H6Typography { FontFamily = MinimalFontStack, FontWeight = "600" },
            Subtitle1 = new Subtitle1Typography { FontFamily = MinimalFontStack },
            Subtitle2 = new Subtitle2Typography { FontFamily = MinimalFontStack },
            Button = new ButtonTypography { FontFamily = MinimalFontStack, FontWeight = "600", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
        }
    };

    // Notion's actual UI stack: system fonts, no webfont.
    private static readonly string[] MinimalFontStack =
    {
        "ui-sans-serif", "-apple-system", "BlinkMacSystemFont", "Segoe UI",
        "Helvetica", "Apple Color Emoji", "Arial", "sans-serif"
    };

    /// <summary>
    /// Resolves the <see cref="MudTheme"/> for a given <see cref="AppTheme"/> selection.
    /// </summary>
    public static MudTheme GetTheme(AppTheme theme) => theme switch
    {
        AppTheme.Minimal => MinimalTheme,
        AppTheme.Dracula => DraculaTheme,
        _ => BrainyTheme,
    };
}
