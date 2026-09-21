namespace KRocketDocumentScanner.Core.Models;

public enum ColorMode
{
    Color,
    Grayscale,
    BlackAndWhiteText,
    BlackAndWhiteClean,
}

public enum ScanSource
{
    Flatbed,
    AutomaticDocumentFeeder,
    AutomaticDocumentFeederDuplex,
}

public readonly record struct ScanArea(double LeftMm, double TopMm, double RightMm, double BottomMm)
{
    public double WidthMm => RightMm - LeftMm;
    public double HeightMm => BottomMm - TopMm;
}

public sealed record ScanOptions
{
    public int ResolutionDpi { get; init; } = 300;
    public ColorMode ColorMode { get; init; } = ColorMode.Color;
    public ScanSource Source { get; init; } = ScanSource.Flatbed;

    public ScanArea? Area { get; init; }

    public bool AutoDeskew { get; init; } = true;
    public bool DropBlankPages { get; init; } = true;
}
