using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.App;

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
                    else
                    {
                        byte v = src[y * page.WidthPx + x];
                        r = g = b = v;
                    }

                    int o = x * 4;
                    destRow[o + 0] = b;
                    destRow[o + 1] = g;
                    destRow[o + 2] = r;
                    destRow[o + 3] = 255;
                }
            }
        }

        return bitmap;
    }
}
