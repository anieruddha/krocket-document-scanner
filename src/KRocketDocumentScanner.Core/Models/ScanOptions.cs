namespace KRocketDocumentScanner.Core.Models;

/// <summary>Color mode for a scan, mirrored from SANE's standard option values.</summary>
public enum ColorMode
{
    Color,
    Grayscale,
    BlackAndWhiteText,
    /// <summary>Captured as grayscale, then reduced to pure black/white by KRocketDocumentScanner itself
    /// (see CapturedPageOps.ToBlackAndWhite) — avoids the scanner's own dithered 1-bit mode.</summary>
    BlackAndWhiteClean,
}

/// <summary>Where the scan is coming from.</summary>
public enum ScanSource
{
    Flatbed,
    AutomaticDocumentFeeder,
    AutomaticDocumentFeederDuplex,
}

/// <summary>A rectangular scan area, in millimeters, relative to the top-left of the page.</summary>
public readonly record struct ScanArea(double LeftMm, double TopMm, double RightMm, double BottomMm)
{
    public double WidthMm => RightMm - LeftMm;
    public double HeightMm => BottomMm - TopMm;
}

/// <summary>All the settings for a single scan operation.</summary>
public sealed record ScanOptions
{
    public int ResolutionDpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public ScanSource Source { get; init; } = ScanSource.Flatbed;

    /// <summary>Null means "full bed / auto" — no explicit crop requested.</summary>
    public ScanArea? Area { get; init; }

    public bool AutoDeskew { get; init; } = true;
    public bool DropBlankPages { get; init; } = true;
}
