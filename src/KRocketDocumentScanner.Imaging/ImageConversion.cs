using KRocketDocumentScanner.Core.Models;
using SkiaSharp;

namespace KRocketDocumentScanner.Imaging;

public static class ImageConversion
{
    public static CapturedPage BitmapToCapturedPage(SKBitmap source)
    {
        using var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(converted))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
        }

        var rgba = converted.Bytes;
        var rgb = new byte[converted.Width * converted.Height * 3];
        for (int src = 0, dst = 0; src < rgba.Length; src += 4, dst += 3)
        {
            rgb[dst] = rgba[src];
            rgb[dst + 1] = rgba[src + 1];
            rgb[dst + 2] = rgba[src + 2];
        }

        return new CapturedPage
        {
            WidthPx = converted.Width,
            HeightPx = converted.Height,
            Format = PixelFormat.Rgb24,
            PixelData = rgb,
        };
    }

    public static CapturedPage DecodeToCapturedPage(Stream encodedImageStream)
    {
        using var bitmap = SKBitmap.Decode(encodedImageStream);
        if (bitmap is null)
            throw new InvalidDataException("Could not decode image data — the stream did not contain a recognizable image format.");
        return BitmapToCapturedPage(bitmap);
    }
}
