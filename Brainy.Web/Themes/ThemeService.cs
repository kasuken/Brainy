using System;

namespace Brainy.Web.Themes;

/// <summary>
/// Identifies one of Brainy's built-in visual themes.
/// </summary>
public enum AppTheme
{
    /// <summary>The warm, editorial default theme.</summary>
    Brainy,

    /// <summary>A flat, Notion-inspired light theme.</summary>
    Minimal,

    /// <summary>A dark theme based on the Dracula color scheme (draculatheme.com).</summary>
    Dracula,
}

/// <summary>
/// Holds the user's active theme selection for the current circuit and
/// notifies subscribers (the layout) when it changes.
/// </summary>
public class ThemeService
{
    public event Action? OnThemeChanged;

    private AppTheme _theme = AppTheme.Brainy;
    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme != value)
            {
                _theme = value;
                OnThemeChanged?.Invoke();
            }
        }
    }
}
