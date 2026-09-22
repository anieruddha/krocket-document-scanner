namespace KRocketDocumentScanner.Core.Models;

public enum PixelFormat
{
    Rgb24,
    Grayscale8,
}

public sealed class CapturedPage
{
    public required int WidthPx { get; init; }
    public required int HeightPx { get; init; }
    public required PixelFormat Format { get; init; }

    public required byte[] PixelData { get; init; }

    public int Dpi { get; init; } = 300;

    public PaperSize? StretchToSheet { get; set; }

    public byte[]? CachedJpeg { get; set; }

    public int BytesPerPixel => Format switch
    {
        PixelFormat.Rgb24 => 3,
        PixelFormat.Grayscale8 => 1,
        _ => throw new NotSupportedException($"Unknown pixel format: {Format}"),
    };

    public int ExpectedByteLength => WidthPx * HeightPx * BytesPerPixel;
}
