using Avalonia.Controls;
using Avalonia.Media;

namespace KRocketDocumentScanner.App.Theming;

public static class ThemeBrushes
{
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
