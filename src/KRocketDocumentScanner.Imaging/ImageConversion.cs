using KRocketDocumentScanner.Core.Models;
using SkiaSharp;

namespace KRocketDocumentScanner.Imaging;

/// <summary>
/// Converts between SkiaSharp bitmaps/encoded image bytes and our own <see cref="CapturedPage"/>
/// format, ALWAYS by forcing a known, explicit pixel layout first rather than trusting whatever
/// native format a source bitmap happens to be in.
///
/// This exists as one shared, single-purpose helper (used identically by both PDF rendering and
/// scanning) specifically because a prior implementation produced a visible blue-tint bug on
/// scanned pages by reading raw pixel bytes without verifying their channel order. Having ONE
/// tested conversion path — instead of two similar-but-separately-written ones — means a future
/// fix here protects both PDF and scanning at once, rather than risking the two copies drifting
/// apart and only one of them getting fixed.
/// </summary>
public static class ImageConversion
{
    /// <summary>
    /// Converts an SKBitmap (of any native color type) to a tightly-packed RGB24
    /// <see cref="CapturedPage"/>, by first explicitly redrawing it into a bitmap of a KNOWN
    /// color type (Rgba8888) via an SKCanvas — never by reading the source bitmap's raw bytes
    /// directly, since its native ColorType is not something calling code should assume.
    /// </summary>
    public static CapturedPage BitmapToCapturedPage(SKBitmap source)
    {
        using var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(converted))
        {
            // Explicit opaque white background: both PDF pages and scanned pages are opaque,
            // and forcing this avoids any transparent/black default showing through.
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
        }

        // Guaranteed tightly-packed RGBA8888 because we specified that SKImageInfo explicitly
        // above — this is NOT an assumption about the source's format, it's a property of the
        // format we just forced it into.
        var rgba = converted.Bytes;
        var rgb = new byte[converted.Width * converted.Height * 3];
        for (int src = 0, dst = 0; src < rgba.Length; src += 4, dst += 3)
        {
            rgb[dst] = rgba[src];         // R
            rgb[dst + 1] = rgba[src + 1]; // G
            rgb[dst + 2] = rgba[src + 2]; // B
            // rgba[src + 3] is alpha — dropped; background was forced opaque above.
        }

        return new CapturedPage
        {
            WidthPx = converted.Width,
            HeightPx = converted.Height,
            Format = PixelFormat.Rgb24,
            PixelData = rgb,
        };
    }

    /// <summary>
    /// Decodes an encoded image (PNG, JPEG, etc.) from a stream and converts it to a
    /// <see cref="CapturedPage"/> via <see cref="BitmapToCapturedPage"/>. Used for sources
    /// (like NAPS2's IMemoryImage) that expose their data only via an encode/save API rather
    /// than a raw in-memory bitmap — decoding through a well-defined file format is itself a
    /// deliberate safety boundary, avoiding any assumption about an in-memory native layout.
    /// </summary>
    public static CapturedPage DecodeToCapturedPage(Stream encodedImageStream)
    {
        using var bitmap = SKBitmap.Decode(encodedImageStream);
        if (bitmap is null)
            throw new InvalidDataException("Could not decode image data — the stream did not contain a recognizable image format.");
        return BitmapToCapturedPage(bitmap);
    }
}
