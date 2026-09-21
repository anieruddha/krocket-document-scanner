using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KRocketDocumentScanner.App;

public static class Converters
{
    /// <summary>true -> the theme's "ready" brush, false -> its "not ready" brush. Reads
    /// live from Application.Resources (populated by Theming/AppTheme.cs) rather than a
    /// hardcoded color, so the status dot follows theme.light.json / theme.dark.json.</summary>
    public static readonly IValueConverter ReadyToBrush =
        new FuncValueConverter<bool, IBrush?>(ready =>
            Application.Current?.FindResource(ready ? "ThemeReadyBrush" : "ThemeNotReadyBrush") as IBrush);

    /// <summary>Inverts a bool — for "show X when NOT busy" style bindings.</summary>
    public static readonly IValueConverter Not =
        new FuncValueConverter<bool, bool>(b => !b);

    /// <summary>true -> the theme's "ready" brush, false -> transparent — for a status dot
    /// that's a filled circle when ready and just an outline (Stroke, set separately) when
    /// not, rather than a filled circle in two different colors.</summary>
    public static readonly IValueConverter ReadyToFillOrTransparent =
        new FuncValueConverter<bool, IBrush?>(ready =>
            ready ? Application.Current?.FindResource("ThemeReadyBrush") as IBrush : Brushes.Transparent);

    /// <summary>true -> the theme's subtle-surface brush, false -> transparent — highlights
    /// the default scanner's whole row instead of (or alongside) a text badge.</summary>
    public static readonly IValueConverter DefaultToRowHighlight =
        new FuncValueConverter<bool, IBrush?>(isDefault =>
            isDefault ? Application.Current?.FindResource("ThemeSurfaceSubtleBrush") as IBrush : Brushes.Transparent);

    /// <summary>true -> a filled star, false -> an outline star — the "make default" action's
    /// own icon doubles as the indicator for which scanner already is the default.</summary>
    public static readonly IValueConverter DefaultToStarGlyph =
        new FuncValueConverter<bool, string>(isDefault => isDefault ? "★" : "☆");
}
