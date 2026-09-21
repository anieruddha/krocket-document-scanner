using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KRocketDocumentScanner.App;

public static class Converters
{
    public static readonly IValueConverter ReadyToBrush =
        new FuncValueConverter<bool, IBrush?>(ready =>
            Application.Current?.FindResource(ready ? "ThemeReadyBrush" : "ThemeNotReadyBrush") as IBrush);

    public static readonly IValueConverter Not =
        new FuncValueConverter<bool, bool>(b => !b);

    public static readonly IValueConverter ReadyToFillOrTransparent =
        new FuncValueConverter<bool, IBrush?>(ready =>
            ready ? Application.Current?.FindResource("ThemeReadyBrush") as IBrush : Brushes.Transparent);

    public static readonly IValueConverter DefaultToRowHighlight =
        new FuncValueConverter<bool, IBrush?>(isDefault =>
            isDefault ? Application.Current?.FindResource("ThemeSurfaceSubtleBrush") as IBrush : Brushes.Transparent);

    public static readonly IValueConverter DefaultToStarGlyph =
        new FuncValueConverter<bool, string>(isDefault => isDefault ? "★" : "☆");
}
