using KRocketDocumentScanner.Core.Models;
using SkiaSharp;

namespace KRocketDocumentScanner.App;

public static class ScanOutputWriter
{
    public const int ReducedJpegQuality = 92;
    private const int FullJpegQuality = 100;

    public static void Save(IReadOnlyList<CapturedPage> pages, string destinationPath, PaperSize? pdfSheet = null,
        bool reduceFileSize = true)
    {
        if (pages.Count == 0) throw new InvalidOperationException("There are no scanned pages to save.");

        var ext = Path.GetExtension(destinationPath).ToLowerInvariant();
        if (ext == ".pdf") SavePdf(pages, destinationPath, pdfSheet, reduceFileSize);
        else SaveImages(pages, destinationPath, ext, reduceFileSize);
    }

    private static void SavePdf(IReadOnlyList<CapturedPage> pages, string destinationPath, PaperSize? sheet, bool reduceFileSize)
    {
        using var stream = File.Create(destinationPath);
        using var document = SKDocument.CreatePdf(stream);

        foreach (var page in pages)
        {
            using var image = reduceFileSize ? ToReducedImage(page) : ToFullImage(page);
            var layout = PdfPageLayout.Compute(page, sheet);
            using var canvas = document.BeginPage(layout.PageWidth, layout.PageHeight);
            if (sheet is not null || page.StretchToSheet is not null) canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(image, SKRect.Create(layout.X, layout.Y, layout.DrawWidth, layout.DrawHeight), SKSamplingOptions.Default, paint);
            document.EndPage();
        }
        document.Close();
    }

    private static SKImage ToReducedImage(CapturedPage page)
    {
        using var jpegData = SKData.CreateCopy(page.CachedJpeg ?? EncodeJpeg(page, ReducedJpegQuality));
        return SKImage.FromEncodedData(jpegData);
    }

    private static SKImage ToFullImage(CapturedPage page)
    {
        using var bitmap = ToSkBitmap(page);
        return SKImage.FromBitmap(bitmap);
    }

    private static void SaveImages(IReadOnlyList<CapturedPage> pages, string destinationPath, string ext, bool reduceFileSize)
    {
        var isJpeg = ext is ".jpg" or ".jpeg";
        var dir = Path.GetDirectoryName(destinationPath)!;
        var baseName = Path.GetFileNameWithoutExtension(destinationPath);

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var path = pages.Count == 1
                ? destinationPath
                : Path.Combine(dir, $"{baseName}-{i + 1:D3}{ext}");

            var bytes = !isJpeg ? EncodePng(page)
                : reduceFileSize ? page.CachedJpeg ?? EncodeJpeg(page, ReducedJpegQuality)
                : EncodeJpeg(page, FullJpegQuality);
            File.WriteAllBytes(path, bytes);
        }
    }

    public static byte[] EncodeJpeg(CapturedPage page, int quality)
    {
        using var bitmap = ToSkBitmap(page);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static byte[] EncodePng(CapturedPage page)
    {
        using var bitmap = ToSkBitmap(page);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
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
