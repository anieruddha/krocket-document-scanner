using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KRocketDocumentScanner.App.Theming;

namespace KRocketDocumentScanner.App;

public sealed class MinimapView : Control
{
    private const double MaxSide = 200;
    private Bitmap? _source;
    private (double L, double T, double R, double B) _sel = (0, 0, 1, 1);
    private Size _bedMm, _pageMm;
    private bool _stretch;

    public bool Stretch
    {
        get => _stretch;
        set { _stretch = value; InvalidateVisual(); }
    }

    public void SetSizes(Size bedMm, Size pageMm)
    {
        if (bedMm == _bedMm && pageMm == _pageMm) return;
        _bedMm = bedMm; _pageMm = pageMm;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Set(Bitmap? source, double l, double t, double r, double b)
    {
        if (ReferenceEquals(source, _source) && _sel == (l, t, r, b)) return;
        _source = source;
        _sel = (l, t, r, b);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private Size CropMm => new((_sel.R - _sel.L) * _bedMm.Width, (_sel.B - _sel.T) * _bedMm.Height);

    private Size PageMmOriented()
    {
        var crop = CropMm;
        bool cropLandscape = crop.Width > crop.Height;
        bool pageLandscape = _pageMm.Width > _pageMm.Height;
        return cropLandscape == pageLandscape ? _pageMm : new Size(_pageMm.Height, _pageMm.Width);
    }

    private Size FrameSize(double availableWidth)
    {
        if (_source is null || _pageMm.Width <= 0 || _pageMm.Height <= 0) return default;
        var page = PageMmOriented();
        double aspect = page.Width / page.Height;
        double mw = aspect >= 1 ? MaxSide : MaxSide * aspect;
        double mh = aspect >= 1 ? MaxSide / aspect : MaxSide;
        if (mw > availableWidth) { mh *= availableWidth / mw; mw = availableWidth; }
        return new Size(mw, mh);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? MaxSide : availableSize.Width;
        return new Size(w, FrameSize(w).Height);
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

    public override void Render(DrawingContext context)
    {
        var frame = FrameSize(Bounds.Width);
        if (_source is null || frame.Width <= 0) return;
        var box = new Rect((Bounds.Width - frame.Width) / 2, 0, frame.Width, frame.Height);
        using var clip = context.PushClip(box);
        context.FillRectangle(ThemeBrushes.Get(this, "ThemeBackgroundBrush"), box);

        var page = PageMmOriented();
        var crop = CropMm;
        double cw = _stretch ? box.Width : box.Width * Math.Min(1, crop.Width / page.Width);
        double ch = _stretch ? box.Height : box.Height * Math.Min(1, crop.Height / page.Height);
        var dest = new Rect(box.X + (box.Width - cw) / 2, box.Y + (box.Height - ch) / 2, cw, ch);

        double imgW = _source.PixelSize.Width, imgH = _source.PixelSize.Height;
        context.DrawImage(_source,
            new Rect(_sel.L * imgW, _sel.T * imgH, (_sel.R - _sel.L) * imgW, (_sel.B - _sel.T) * imgH), dest);
        context.DrawRectangle(new Pen(ThemeBrushes.Get(this, "ThemeBorderStrongBrush"), 1), box);
    }
}
