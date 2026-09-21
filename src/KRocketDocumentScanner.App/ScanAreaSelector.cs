using KRocketDocumentScanner.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.Theming;

namespace KRocketDocumentScanner.App;

/// <summary>
/// A draggable/resizable crop-area rectangle over a preview scan image. Custom-drawn (no
/// XAML) since this needs real pointer-drag handling that plain control composition can't
/// give it. Reports its selection as a normalized [0,1] rectangle relative to the image, so
/// it stays meaningful regardless of the image's actual pixel size or the window's size.
/// </summary>
public sealed class ScanAreaSelector : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<ScanAreaSelector, Bitmap?>(nameof(Source));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private double _selLeft, _selTop, _selRight = 1, _selBottom = 1;
    public (double Left, double Top, double Right, double Bottom) SelectionNormalized => (_selLeft, _selTop, _selRight, _selBottom);

    /// <summary>Raised after a drag completes — the moment to read <see cref="SelectionNormalized"/>.</summary>
    public event EventHandler? SelectionChanged;

    private enum DragMode { None, Move, TopLeft, TopRight, BottomLeft, BottomRight, Pan }
    private DragMode _dragMode = DragMode.None;
    private Point _dragStartPoint;
    private (double L, double T, double R, double B) _selAtDragStart;

    private const double HandleSize = 10;
    private const double MinSelectionSize = 0.02;

    // Set whenever a page-size preset (A4/Letter/...) is active — corner-drag resizing then
    // keeps this width:height ratio instead of resizing freely. In the SAME normalized [0,1]
    // coordinate space the selection itself uses (not raw mm), since the bed isn't necessarily
    // square — see ScanViewModel.AspectLockChanged.
    private double? _lockedAspect;
    // Size range (fraction of image width) a locked-shape crop may be resized within.
    private double _lockMinWidth, _lockMaxWidth = 1;

    // ---- Zoom / pan ----
    // _zoom 1 = the whole image fits the pane (the default). Above that, the view is centred on
    // (_viewCx, _viewCy), a point in the image's own normalized [0,1] space. The minimap in
    // the top-right corner shows just the cropped part of the scan (display only).
    private const double MaxZoom = 8;
    private const double WheelZoomStep = 1.2;
    private double _zoom = 1, _viewCx = 0.5, _viewCy = 0.5;
    private Point _panStartPoint;
    private (double Cx, double Cy) _panStartCenter;

    /// <summary>Overview control fed with the current scan and crop on every render.</summary>
    public MinimapView? Minimap { get; set; }

    static ScanAreaSelector()
    {
        AffectsRender<ScanAreaSelector>(SourceProperty);
    }

    private void OnThemeApplied() => InvalidateVisual();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppTheme.Applied += OnThemeApplied;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppTheme.Applied -= OnThemeApplied;
        base.OnDetachedFromVisualTree(e);
    }

    public ScanAreaSelector()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheel;
    }

    /// <summary>Resets to the full, uncropped image — called whenever a new preview loads,
    /// matching what a fresh preview should mean (nothing cropped yet).</summary>
    public void ResetSelection()
    {
        _selLeft = 0; _selTop = 0; _selRight = 1; _selBottom = 1;
        _lockedAspect = null;
        _lockMinWidth = 0; _lockMaxWidth = 1;
        _zoom = 1; _viewCx = 0.5; _viewCy = 0.5;
        InvalidateVisual();
    }

    /// <summary>Programmatically sets the crop selection — used by page-size presets
    /// (A4/Letter/Legal/...), as opposed to the user dragging it directly.</summary>
    public void SetSelectionNormalized(double left, double top, double right, double bottom)
    {
        _selLeft = left; _selTop = top; _selRight = right; _selBottom = bottom;
        InvalidateVisual();
    }

    /// <summary>Locks (a width:height ratio, normalized-space) or unlocks (null) corner-drag
    /// resizing — see ScanViewModel.AspectLockChanged for why the ratio lives in this space.</summary>
    public void SetLockedAspectRatio(AspectLock? aspectLock)
    {
        _lockedAspect = aspectLock?.Ratio;
        _lockMinWidth = aspectLock?.MinWidth ?? 0;
        _lockMaxWidth = aspectLock?.MaxWidth ?? 1;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        // Fully-transparent fill over the whole control so pointer events register everywhere,
        // not just where something opaque is drawn.
        context.FillRectangle(Brushes.Transparent, new Rect(bounds.Size));

        if (Source is null)
        {
            var text = new FormattedText(
                Strings.PreviewPlaceholder,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                13,
                ThemeBrushes.Get(this, "ThemeTextMutedBrush"));
            context.DrawText(text, new Point(Math.Max(0, bounds.Width / 2 - text.Width / 2), Math.Max(0, bounds.Height / 2 - text.Height / 2)));
            return;
        }

        using var clip = context.PushClip(new Rect(bounds.Size));
        var (ox, oy, w, h) = ImageRect();
        context.DrawImage(Source, new Rect(0, 0, Source.PixelSize.Width, Source.PixelSize.Height), new Rect(ox, oy, w, h));

        double sx0 = ox + _selLeft * w, sy0 = oy + _selTop * h;
        double sx1 = ox + _selRight * w, sy1 = oy + _selBottom * h;

        var dim = ThemeBrushes.Get(this, "ThemeCropDimBrush");
        context.FillRectangle(dim, new Rect(ox, oy, w, Math.Max(0, sy0 - oy)));                    // top
        context.FillRectangle(dim, new Rect(ox, sy1, w, Math.Max(0, (oy + h) - sy1)));              // bottom
        context.FillRectangle(dim, new Rect(ox, sy0, Math.Max(0, sx0 - ox), Math.Max(0, sy1 - sy0)));// left
        context.FillRectangle(dim, new Rect(sx1, sy0, Math.Max(0, (ox + w) - sx1), Math.Max(0, sy1 - sy0))); // right

        var selectionBrush = ThemeBrushes.Get(this, "ThemeAccentBrush");
        var pen = new Pen(selectionBrush, 2);
        context.DrawRectangle(pen, new Rect(sx0, sy0, sx1 - sx0, sy1 - sy0));

        foreach (var (hx, hy) in new[] { (sx0, sy0), (sx1, sy0), (sx0, sy1), (sx1, sy1) })
        {
            context.FillRectangle(selectionBrush, new Rect(hx - HandleSize / 2, hy - HandleSize / 2, HandleSize, HandleSize));
        }

        // Invalidating another control isn't allowed mid-render, so hand the state over afterwards.
        if (Minimap is { } minimap)
        {
            var (src, l, t, r, b) = (Source, _selLeft, _selTop, _selRight, _selBottom);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => minimap.Set(src, l, t, r, b));
        }
    }

    /// <summary>The on-screen rect the whole image occupies within the control: fit-to-control
    /// (centered, aspect preserved) at zoom 1, larger and panned when zoomed in. When zoomed
    /// past the pane, it may extend beyond the control (the control clips it).</summary>
    private (double ox, double oy, double w, double h) ImageRect()
    {
        var bounds = Bounds;
        if (Source is null || bounds.Width <= 0 || bounds.Height <= 0) return (0, 0, bounds.Width, bounds.Height);
        double imgW = Source.PixelSize.Width, imgH = Source.PixelSize.Height;
        double scale = Math.Min(bounds.Width / imgW, bounds.Height / imgH) * _zoom;
        double w = imgW * scale, h = imgH * scale;
        var (cx, cy) = ClampedCenter(w, h);
        return (bounds.Width / 2 - cx * w, bounds.Height / 2 - cy * h, w, h);
    }

    /// <summary>The view centre kept inside the image: centered on an axis where the image is
    /// smaller than the pane, otherwise limited so no empty space shows past an edge.</summary>
    private (double cx, double cy) ClampedCenter(double w, double h)
    {
        double cx = _viewCx, cy = _viewCy;
        if (w <= Bounds.Width) cx = 0.5;
        else { double half = Bounds.Width / 2 / w; cx = Math.Clamp(cx, half, 1 - half); }
        if (h <= Bounds.Height) cy = 0.5;
        else { double half = Bounds.Height / 2 / h; cy = Math.Clamp(cy, half, 1 - half); }
        return (cx, cy);
    }

    private void SetViewCenter(double cx, double cy)
    {
        _viewCx = cx; _viewCy = cy;
        var (_, _, w, h) = ImageRect();
        (_viewCx, _viewCy) = ClampedCenter(w, h);
        InvalidateVisual();
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Source is null) return;
        var p = e.GetPosition(this);
        var (ox, oy, w, h) = ImageRect();
        // The image point under the cursor stays under the cursor while zooming.
        double nx = (p.X - ox) / w, ny = (p.Y - oy) / h;

        _zoom = Math.Clamp(_zoom * Math.Pow(WheelZoomStep, e.Delta.Y), 1, MaxZoom);
        if (_zoom <= 1.001) { _zoom = 1; SetViewCenter(0.5, 0.5); e.Handled = true; return; }

        var (_, _, w2, h2) = ImageRect();
        SetViewCenter(nx + (Bounds.Width / 2 - p.X) / w2, ny + (Bounds.Height / 2 - p.Y) / h2);
        e.Handled = true;
    }

    private DragMode HitTest(Point p)
    {
        var (ox, oy, w, h) = ImageRect();
        double sx0 = ox + _selLeft * w, sy0 = oy + _selTop * h;
        double sx1 = ox + _selRight * w, sy1 = oy + _selBottom * h;

        bool Near(double ax, double ay) => Math.Abs(p.X - ax) < HandleSize && Math.Abs(p.Y - ay) < HandleSize;

        if (Near(sx0, sy0)) return DragMode.TopLeft;
        if (Near(sx1, sy0)) return DragMode.TopRight;
        if (Near(sx0, sy1)) return DragMode.BottomLeft;
        if (Near(sx1, sy1)) return DragMode.BottomRight;
        if (p.X >= sx0 && p.X <= sx1 && p.Y >= sy0 && p.Y <= sy1) return DragMode.Move;
        return DragMode.None;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Source is null) return;
        _dragStartPoint = e.GetPosition(this);

        bool middle = e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;
        _dragMode = middle ? DragMode.None : HitTest(_dragStartPoint);
        // Grabbing empty space (or the middle button) pans, but only once zoomed in.
        if (_dragMode == DragMode.None && _zoom > 1.001)
        {
            _dragMode = DragMode.Pan;
            _panStartPoint = _dragStartPoint;
            _panStartCenter = (_viewCx, _viewCy);
            e.Pointer.Capture(this);
            return;
        }
        if (_dragMode == DragMode.None) return;
        _selAtDragStart = (_selLeft, _selTop, _selRight, _selBottom);
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None || Source is null) return;
        var (ox, oy, w, h) = ImageRect();
        if (w <= 0 || h <= 0) return;

        var current = e.GetPosition(this);

        if (_dragMode == DragMode.Pan)
        {
            SetViewCenter(_panStartCenter.Cx - (current.X - _panStartPoint.X) / w,
                          _panStartCenter.Cy - (current.Y - _panStartPoint.Y) / h);
            return;
        }

        double dx = (current.X - _dragStartPoint.X) / w;
        double dy = (current.Y - _dragStartPoint.Y) / h;
        var (l, t, r, b) = _selAtDragStart;

        switch (_dragMode)
        {
            case DragMode.Move:
                double width = r - l, height = b - t;
                double nl = Math.Clamp(l + dx, 0, 1 - width);
                double nt = Math.Clamp(t + dy, 0, 1 - height);
                _selLeft = nl; _selTop = nt; _selRight = nl + width; _selBottom = nt + height;
                break;
            case DragMode.TopLeft:
            case DragMode.TopRight:
            case DragMode.BottomLeft:
            case DragMode.BottomRight:
                ApplyCornerResize(_dragMode, dx, dy, (l, t, r, b));
                break;
        }
        InvalidateVisual();
    }

    /// <summary>Resizes from one dragged corner. With no aspect lock, each edge moves
    /// independently (the original free-resize behavior). Locked (a preset is active), only
    /// the dragged corner's horizontal movement drives the size — the other dimension is
    /// derived from the locked ratio — so the shape can never drift off the preset's exact
    /// proportions, anchored at the opposite (fixed) corner.</summary>
    private void ApplyCornerResize(DragMode mode, double dx, double dy, (double L, double T, double R, double B) start)
    {
        var (l, t, r, b) = start;

        if (_lockedAspect is not { } aspect)
        {
            switch (mode)
            {
                case DragMode.TopLeft:
                    _selLeft = Math.Clamp(l + dx, 0, r - MinSelectionSize);
                    _selTop = Math.Clamp(t + dy, 0, b - MinSelectionSize);
                    break;
                case DragMode.TopRight:
                    _selRight = Math.Clamp(r + dx, l + MinSelectionSize, 1);
                    _selTop = Math.Clamp(t + dy, 0, b - MinSelectionSize);
                    break;
                case DragMode.BottomLeft:
                    _selLeft = Math.Clamp(l + dx, 0, r - MinSelectionSize);
                    _selBottom = Math.Clamp(b + dy, t + MinSelectionSize, 1);
                    break;
                case DragMode.BottomRight:
                    _selRight = Math.Clamp(r + dx, l + MinSelectionSize, 1);
                    _selBottom = Math.Clamp(b + dy, t + MinSelectionSize, 1);
                    break;
            }
            return;
        }

        switch (mode)
        {
            case DragMode.BottomRight:
            {
                double maxW = Math.Min(1 - l, (1 - t) * aspect);
                double w = ClampLockedWidth(r - l + dx, maxW);
                _selLeft = l; _selTop = t; _selRight = l + w; _selBottom = t + w / aspect;
                break;
            }
            case DragMode.TopLeft:
            {
                double maxW = Math.Min(r, b * aspect);
                double w = ClampLockedWidth(r - l - dx, maxW);
                _selRight = r; _selBottom = b; _selLeft = r - w; _selTop = b - w / aspect;
                break;
            }
            case DragMode.TopRight:
            {
                double maxW = Math.Min(1 - l, b * aspect);
                double w = ClampLockedWidth(r - l + dx, maxW);
                _selLeft = l; _selBottom = b; _selRight = l + w; _selTop = b - w / aspect;
                break;
            }
            case DragMode.BottomLeft:
            {
                double maxW = Math.Min(r, (1 - t) * aspect);
                double w = ClampLockedWidth(r - l - dx, maxW);
                _selRight = r; _selTop = t; _selLeft = r - w; _selBottom = t + w / aspect;
                break;
            }
        }
    }

    /// <summary>Clamps a locked-shape crop's width to its allowed range (the preset's 70–100%)
    /// and to what still fits inside the image from the fixed corner.</summary>
    private double ClampLockedWidth(double width, double maxFitWidth)
    {
        double lo = Math.Max(MinSelectionSize, _lockMinWidth);
        double hi = Math.Max(lo, Math.Min(maxFitWidth, _lockMaxWidth));
        return Math.Clamp(width, lo, hi);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Panning only moves the view, never the crop.
        bool wasDragging = _dragMode is not (DragMode.None or DragMode.Pan);
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        if (wasDragging) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
