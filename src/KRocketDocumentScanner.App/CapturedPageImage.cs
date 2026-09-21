using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Converts our own <see cref="CapturedPage"/> (tightly-packed RGB24 or Grayscale8) into an
/// Avalonia bitmap for display.
///
/// Avalonia's WriteableBitmap wants 32-bit BGRA, so the channel reordering happens here,
/// explicitly and in one place. This is the same discipline as KRocketDocumentScanner.Imaging: never assume
/// a buffer's channel order matches what the target expects — a wrong assumption here is
/// exactly what produces a red/blue-swapped image, which is the bug class that showed up as a
/// blue tint in an earlier prototype.
/// </summary>
public static class CapturedPageImage
{
    public static Bitmap ToBitmap(CapturedPage page)
    {
        var size = new PixelSize(page.WidthPx, page.HeightPx);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, AlphaFormat.Opaque);

        using var buffer = bitmap.Lock();
        unsafe
        {
            byte* dest = (byte*)buffer.Address;
            var src = page.PixelData;
            int rowBytes = buffer.RowBytes;

            for (int y = 0; y < page.HeightPx; y++)
            {
                byte* destRow = dest + (y * rowBytes);
                for (int x = 0; x < page.WidthPx; x++)
                {
                    byte r, g, b;
                    if (page.Format == KRocketDocumentScanner.Core.Models.PixelFormat.Rgb24)
                    {
                        int i = (y * page.WidthPx + x) * 3;
                        r = src[i]; g = src[i + 1]; b = src[i + 2];
                    }
                    else // Grayscale8 — one value drives all three channels
                    {
                        byte v = src[y * page.WidthPx + x];
                        r = g = b = v;
                    }

                    int o = x * 4;
                    destRow[o + 0] = b;   // BGRA order, not RGBA — the reordering this class exists for
                    destRow[o + 1] = g;
                    destRow[o + 2] = r;
                    destRow[o + 3] = 255; // opaque
                }
            }
        }

        return bitmap;
    }
}
