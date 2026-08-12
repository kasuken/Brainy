using System;

namespace Brainy.Web.Themes;

public class ThemeService
{
    public event Action? OnThemeChanged;
    
    private bool _isMinimalTheme = false;
    public bool IsMinimalTheme
    {
        get => _isMinimalTheme;
        set
        {
            if (_isMinimalTheme != value)
            {
                _isMinimalTheme = value;
                OnThemeChanged?.Invoke();
            }
        }
    }
}
