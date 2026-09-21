using KRocketDocumentScanner.Core.Models;
using SkiaSharp;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Writes captured pages to disk as either a multi-page PDF or image file(s).
/// Uses SkiaSharp's own PDF document support, which is already a dependency via
/// KRocketDocumentScanner.Imaging — no additional PDF-writing library needed.
/// </summary>
public static class ScanOutputWriter
{
    public static void Save(IReadOnlyList<CapturedPage> pages, string destinationPath, PaperSize? pdfSheet = null)
    {
        if (pages.Count == 0) throw new InvalidOperationException("There are no scanned pages to save.");

        var ext = Path.GetExtension(destinationPath).ToLowerInvariant();
        if (ext == ".pdf") SavePdf(pages, destinationPath, pdfSheet);
        else SaveImages(pages, destinationPath, ext);
    }

    private static void SavePdf(IReadOnlyList<CapturedPage> pages, string destinationPath, PaperSize? sheet)
    {
        using var stream = File.Create(destinationPath);
        using var document = SKDocument.CreatePdf(stream);

        foreach (var page in pages)
        {
            using var bitmap = ToSkBitmap(page);
            var layout = PdfPageLayout.Compute(page, sheet);
            using var canvas = document.BeginPage(layout.PageWidth, layout.PageHeight);
            if (sheet is not null || page.StretchToSheet is not null) canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bitmap, SKRect.Create(layout.X, layout.Y, layout.DrawWidth, layout.DrawHeight), paint);
            document.EndPage();
        }
        document.Close();
    }

    private static void SaveImages(IReadOnlyList<CapturedPage> pages, string destinationPath, string ext)
    {
        var format = ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Png,
        };

        var dir = Path.GetDirectoryName(destinationPath)!;
        var baseName = Path.GetFileNameWithoutExtension(destinationPath);

        for (int i = 0; i < pages.Count; i++)
        {
            // Single page keeps the exact filename chosen; multiple pages get numbered
            // suffixes, since one image file can't hold several pages.
            var path = pages.Count == 1
                ? destinationPath
                : Path.Combine(dir, $"{baseName}-{i + 1:D3}{ext}");

            using var bitmap = ToSkBitmap(pages[i]);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 92);
            using var fs = File.Create(path);
            data.SaveTo(fs);
        }
    }

    private static SKBitmap ToSkBitmap(CapturedPage page)
    {
        var bitmap = new SKBitmap(new SKImageInfo(page.WidthPx, page.HeightPx, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = bitmap.GetPixelSpan();

        unsafe
        {
            fixed (byte* target = &System.Runtime.InteropServices.MemoryMarshal.GetReference(pixels))
            {
                for (int i = 0, n = page.WidthPx * page.HeightPx; i < n; i++)
                {
                    byte r, g, b;
                    if (page.Format == KRocketDocumentScanner.Core.Models.PixelFormat.Rgb24)
                    {
                        int s = i * 3;
                        r = page.PixelData[s]; g = page.PixelData[s + 1]; b = page.PixelData[s + 2];
                    }
                    else
                    {
                        r = g = b = page.PixelData[i];
                    }
                    int d = i * 4;
                    target[d + 0] = r;
                    target[d + 1] = g;
                    target[d + 2] = b;
                    target[d + 3] = 255;
                }
            }
        }

        return bitmap;
    }
}
