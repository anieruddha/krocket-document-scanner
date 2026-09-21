namespace KRocketDocumentScanner.Core.Models;

/// <summary>
/// Pure pixel-data operations on <see cref="CapturedPage"/>. Kept dependency-free (no imaging
/// library) so it's directly testable and usable from any layer.
/// </summary>
public static class CapturedPageOps
{
    /// <summary>
    /// Crops to the pixel rectangle [left, top, right, bottom) (right/bottom exclusive).
    /// Used to implement "selectable scan area": the underlying scanning engine doesn't expose
    /// an arbitrary crop rectangle option directly, so the full page/bed is captured and then
    /// cropped here to the region the user selected in the preview.
    /// </summary>
    public static CapturedPage Crop(CapturedPage source, int left, int top, int right, int bottom)
    {
        if (left < 0 || top < 0 || right > source.WidthPx || bottom > source.HeightPx || left >= right || top >= bottom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(left), $"Crop rectangle ({left},{top},{right},{bottom}) is invalid for a {source.WidthPx}x{source.HeightPx} image.");
        }

        int bpp = source.BytesPerPixel;
        int cropWidth = right - left;
        int cropHeight = bottom - top;
        int srcStride = source.WidthPx * bpp;
        int dstStride = cropWidth * bpp;

        var dst = new byte[cropWidth * cropHeight * bpp];
        for (int row = 0; row < cropHeight; row++)
        {
            int srcOffset = (top + row) * srcStride + left * bpp;
            int dstOffset = row * dstStride;
            Array.Copy(source.PixelData, srcOffset, dst, dstOffset, dstStride);
        }

        return new CapturedPage
        {
            WidthPx = cropWidth,
            HeightPx = cropHeight,
            Format = source.Format,
            PixelData = dst,
            Dpi = source.Dpi,
        };
    }

    /// <summary>Same pixels, tagged with the resolution they were captured at.</summary>
    public static CapturedPage WithDpi(CapturedPage source, int dpi) => new()
    {
        WidthPx = source.WidthPx,
        HeightPx = source.HeightPx,
        Format = source.Format,
        PixelData = source.PixelData,
        Dpi = dpi,
    };

    /// <summary>
    /// Reduces the page to pure black and white with a single automatic (Otsu) threshold: no
    /// blur, sharpening or other filtering, just a per-pixel black/white decision. Result is
    /// Grayscale8 containing only 0 and 255.
    /// </summary>
    public static CapturedPage ToBlackAndWhite(CapturedPage source)
    {
        int count = source.WidthPx * source.HeightPx;
        var lum = new byte[count];
        if (source.Format == PixelFormat.Rgb24)
        {
            for (int i = 0; i < count; i++)
            {
                int s = i * 3;
                lum[i] = (byte)((source.PixelData[s] * 299 + source.PixelData[s + 1] * 587 + source.PixelData[s + 2] * 114) / 1000);
            }
        }
        else
        {
            Array.Copy(source.PixelData, lum, count);
        }

        var hist = new long[256];
        foreach (var v in lum) hist[v]++;

        // Otsu: the cutoff that maximizes the variance between the dark and light groups.
        double sumAll = 0;
        for (int i = 0; i < 256; i++) sumAll += i * (double)hist[i];
        double sumDark = 0, best = -1;
        long weightDark = 0;
        int threshold = 127;
        for (int t = 0; t < 256; t++)
        {
            weightDark += hist[t];
            if (weightDark == 0) continue;
            long weightLight = count - weightDark;
            if (weightLight == 0) break;
            sumDark += t * (double)hist[t];
            double meanDark = sumDark / weightDark, meanLight = (sumAll - sumDark) / weightLight;
            double between = weightDark * (double)weightLight * (meanDark - meanLight) * (meanDark - meanLight);
            if (between > best) { best = between; threshold = t; }
        }

        for (int i = 0; i < count; i++) lum[i] = lum[i] > threshold ? (byte)255 : (byte)0;

        return new CapturedPage
        {
            WidthPx = source.WidthPx,
            HeightPx = source.HeightPx,
            Format = PixelFormat.Grayscale8,
            PixelData = lum,
            Dpi = source.Dpi,
        };
    }

    /// <summary>
    /// Converts a normalized [0,1] selection rectangle (as produced by a preview-image area
    /// selector UI) into pixel coordinates for a page of the given size, clamped to valid bounds.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) NormalizedToPixelRect(
        double normLeft, double normTop, double normRight, double normBottom, int widthPx, int heightPx)
    {
        int left = Math.Clamp((int)Math.Round(normLeft * widthPx), 0, widthPx);
        int top = Math.Clamp((int)Math.Round(normTop * heightPx), 0, heightPx);
        int right = Math.Clamp((int)Math.Round(normRight * widthPx), 0, widthPx);
        int bottom = Math.Clamp((int)Math.Round(normBottom * heightPx), 0, heightPx);

        // Guarantee a non-empty rectangle even from degenerate input (e.g. a zero-size selection).
        if (right <= left) right = Math.Min(left + 1, widthPx);
        if (bottom <= top) bottom = Math.Min(top + 1, heightPx);

        return (left, top, right, bottom);
    }
}
