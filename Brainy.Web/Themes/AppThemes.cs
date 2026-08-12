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

    public static MudTheme MinimalTheme => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#000000",
            PrimaryContrastText = "#ffffff",
            Secondary = "#f1f1f0",
            SecondaryContrastText = "#000000",
            Tertiary = "#e0e0e0",
            Background = "#ffffff",
            BackgroundGray = "#f7f7f5",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#000000",
            DrawerBackground = "#f7f7f5",
            DrawerText = "#37352f",
            DrawerIcon = "#37352f",
            TextPrimary = "#37352f",
            TextSecondary = "#787774",
            ActionDefault = "#787774",
            Divider = "#e9e9e7",
            LinesDefault = "#e9e9e7",
            LinesInputs = "#e9e9e7",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" } },
            H1 = new H1Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "700" },
            H2 = new H2Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "600" },
            H3 = new H3Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "600" },
            H4 = new H4Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "600" },
            H5 = new H5Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "500" },
            H6 = new H6Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "500" },
            Subtitle1 = new Subtitle1Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" } },
            Subtitle2 = new Subtitle2Typography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" } },
            Button = new ButtonTypography { FontFamily = new[] { "Inter", "Segoe UI", "sans-serif" }, FontWeight = "500", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "4px",
        }
    };
}
