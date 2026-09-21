namespace KRocketDocumentScanner.Core.Models;

public sealed record PaperSize(string Name, double WidthMm, double HeightMm);

public sealed record DetectedPaper(PaperSize Size, bool Landscape, int LeftPx, int TopPx, int RightPx, int BottomPx);

public static class PaperDetector
{
    public static readonly IReadOnlyList<PaperSize> KnownSizes = new[]
    {
        new PaperSize("A4", 210.0, 297.0),
        new PaperSize("Letter", 215.9, 279.4),
        new PaperSize("Legal", 215.9, 355.6),
        new PaperSize("A5", 148.0, 210.0),
    };

    private const double SizeTolerance = 0.04;
    private const int DifferenceThreshold = 28;
    private const int MaxSampleSide = 400;

    public static DetectedPaper? Detect(CapturedPage page, int dpi)
    {
        if (dpi <= 0 || page.WidthPx < 20 || page.HeightPx < 20) return null;

        int step = Math.Max(1, Math.Max(page.WidthPx, page.HeightPx) / MaxSampleSide);
        int gw = page.WidthPx / step, gh = page.HeightPx / step;
        var lum = new byte[gw * gh];
        int bpp = page.BytesPerPixel;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = ((y * step) * page.WidthPx + x * step) * bpp;
                lum[y * gw + x] = bpp == 1
                    ? page.PixelData[i]
                    : (byte)((page.PixelData[i] * 299 + page.PixelData[i + 1] * 587 + page.PixelData[i + 2] * 114) / 1000);
            }

        var ring = new List<byte>();
        for (int x = 0; x < gw; x++) { ring.Add(lum[x]); ring.Add(lum[(gh - 1) * gw + x]); }
        for (int y = 0; y < gh; y++) { ring.Add(lum[y * gw]); ring.Add(lum[y * gw + gw - 1]); }
        ring.Sort();
        int bg = ring[ring.Count / 2];

        var colCount = new int[gw];
        var rowCount = new int[gh];
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
                if (Math.Abs(lum[y * gw + x] - bg) > DifferenceThreshold) { colCount[x]++; rowCount[y]++; }

        int maxCol = colCount.Max(), maxRow = rowCount.Max();
        if (maxCol < gh / 10 || maxRow < gw / 10) return null;

        int x0 = Array.FindIndex(colCount, c => c >= maxCol / 2);
        int x1 = Array.FindLastIndex(colCount, c => c >= maxCol / 2) + 1;
        int y0 = Array.FindIndex(rowCount, c => c >= maxRow / 2);
        int y1 = Array.FindLastIndex(rowCount, c => c >= maxRow / 2) + 1;
        if (x1 - x0 < 5 || y1 - y0 < 5) return null;

        long filled = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (Math.Abs(lum[y * gw + x] - bg) > DifferenceThreshold) filled++;
        if (filled < 0.75 * (x1 - x0) * (y1 - y0)) return null;

        double wMm = (x1 - x0) * step / (double)dpi * 25.4;
        double hMm = (y1 - y0) * step / (double)dpi * 25.4;

        foreach (var size in KnownSizes)
        {
            bool portrait = Near(wMm, size.WidthMm) && Near(hMm, size.HeightMm);
            bool landscape = Near(wMm, size.HeightMm) && Near(hMm, size.WidthMm);
            if (portrait || landscape)
                return new DetectedPaper(size, !portrait, x0 * step, y0 * step, x1 * step, y1 * step);
        }
        return null;
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= expected * SizeTolerance;
}
