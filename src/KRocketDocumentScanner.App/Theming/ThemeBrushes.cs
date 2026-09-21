using Avalonia.Controls;
using Avalonia.Media;

namespace KRocketDocumentScanner.App.Theming;

/// <summary>Resolves Theme*Brush resources for custom-drawn controls, which can't use
/// {DynamicResource}. Call at render time and redraw on <see cref="AppTheme.Applied"/>.
/// A missing key draws nothing (transparent) rather than a made-up colour.</summary>
public static class ThemeBrushes
{
    /// <summary>For code-built controls that aren't attached yet: the app-level resource.</summary>
    public static IBrush Get(string key)
    {
        var app = Avalonia.Application.Current;
        return app is not null && app.TryFindResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : Brushes.Transparent;
    }

    public static IBrush Get(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush : Brushes.Transparent;
}
