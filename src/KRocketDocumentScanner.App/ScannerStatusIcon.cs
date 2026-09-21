using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.App;

public sealed class ScannerStatusIcon : Control
{
    private const double IconSize = 14;

    public static readonly StyledProperty<ScannerReachability> ReachabilityProperty =
        AvaloniaProperty.Register<ScannerStatusIcon, ScannerReachability>(nameof(Reachability));

    static ScannerStatusIcon() => AffectsRender<ScannerStatusIcon>(ReachabilityProperty);

    public ScannerReachability Reachability
    {
        get => GetValue(ReachabilityProperty);
        set => SetValue(ReachabilityProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);

    public override void Render(DrawingContext context)
    {
        const double r = IconSize / 2;
        var center = new Point(r, r);

        switch (Reachability)
        {
            case ScannerReachability.Ready:
            {
                context.DrawEllipse(ThemeBrush("ThemeReadyBrush"), null, center, r, r);
                var pen = new Pen(Brushes.White, 1.7, lineCap: PenLineCap.Round);
                context.DrawLine(pen, new Point(3.8, 7.4), new Point(6.2, 9.8));
                context.DrawLine(pen, new Point(6.2, 9.8), new Point(10.4, 4.8));
                break;
            }
            case ScannerReachability.NotReady:
            {
                context.DrawEllipse(ThemeBrush("ThemeDangerBrush"), null, center, r, r);
                var pen = new Pen(Brushes.White, 1.7, lineCap: PenLineCap.Round);
                context.DrawLine(pen, new Point(4.7, 4.7), new Point(9.3, 9.3));
                context.DrawLine(pen, new Point(9.3, 4.7), new Point(4.7, 9.3));
                break;
            }
            default:
                context.DrawEllipse(null, new Pen(ThemeBrush("ThemeNotReadyBrush"), 1.6), center, r - 0.8, r - 0.8);
                break;
        }
    }

    private static IBrush ThemeBrush(string key) =>
        Application.Current?.FindResource(key) as IBrush ?? Brushes.Gray;
}
