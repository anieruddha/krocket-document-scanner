namespace KRocketDocumentScanner.Core.Models;

/// <summary>Pixel format of a captured page's raw data.</summary>
public enum PixelFormat
{
    Rgb24,       // 3 bytes/pixel, tightly packed, no padding
    Grayscale8,  // 1 byte/pixel
}

/// <summary>
/// A single captured page: raw pixel data plus enough metadata to reconstruct it
/// unambiguously. Keeping this explicit (rather than assuming a library's own image type)
/// is what lets color-conversion code be reasoned about and tested directly.
/// </summary>
public sealed class CapturedPage
{
    public required int WidthPx { get; init; }
    public required int HeightPx { get; init; }
    public required PixelFormat Format { get; init; }

    /// <summary>Row-major, tightly packed (no row padding/stride gaps).</summary>
    public required byte[] PixelData { get; init; }

    /// <summary>Resolution the page was captured at; gives the pixels a physical size (used
    /// for PDF page dimensions). 300 when unknown.</summary>
    public int Dpi { get; init; } = 300;

    /// <summary>When set, the page was captured with "Stretch to page" on: in a PDF it fills a
    /// page of this paper size (turned to match the scan's orientation) instead of being placed
    /// at true size. Fixed when the page is captured — later toggling doesn't touch it.</summary>
    public PaperSize? StretchToSheet { get; set; }

    public int BytesPerPixel => Format switch
    {
        PixelFormat.Rgb24 => 3,
        PixelFormat.Grayscale8 => 1,
        _ => throw new NotSupportedException($"Unknown pixel format: {Format}"),
    };

    public int ExpectedByteLength => WidthPx * HeightPx * BytesPerPixel;
}
