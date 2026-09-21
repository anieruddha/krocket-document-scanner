namespace KRocketDocumentScanner.Core.Models;

/// <summary>Where a page's image goes on its PDF page, all in PDF points (1/72 inch).</summary>
public readonly record struct PdfPageLayout(float PageWidth, float PageHeight, float X, float Y, float DrawWidth, float DrawHeight)
{
    private const double PointsPerMm = 72.0 / 25.4;

    /// <summary>Layout of one page. With no sheet, the PDF page is exactly the scan's physical
    /// size. With a sheet, the PDF page is that paper size (turned to match the scan's
    /// orientation) and the scan is centred on it at true size — reduced only if it's bigger
    /// than the sheet, never enlarged. A page marked <see cref="CapturedPage.StretchToSheet"/>
    /// instead fills a page of that sheet's size edge to edge.</summary>
    public static PdfPageLayout Compute(CapturedPage page, PaperSize? sheet)
    {
        int dpi = page.Dpi > 0 ? page.Dpi : 300;
        double imgW = page.WidthPx / (double)dpi * 72.0;
        double imgH = page.HeightPx / (double)dpi * 72.0;

        // A page captured with "Stretch to page" always gets its own sheet and fills it.
        bool stretch = page.StretchToSheet is not null;
        sheet = page.StretchToSheet ?? sheet;

        if (sheet is null)
            return new PdfPageLayout((float)imgW, (float)imgH, 0, 0, (float)imgW, (float)imgH);

        double shortSide = Math.Min(sheet.WidthMm, sheet.HeightMm) * PointsPerMm;
        double longSide = Math.Max(sheet.WidthMm, sheet.HeightMm) * PointsPerMm;
        bool landscape = page.WidthPx > page.HeightPx;
        double sheetW = landscape ? longSide : shortSide;
        double sheetH = landscape ? shortSide : longSide;

        if (stretch)
            return new PdfPageLayout((float)sheetW, (float)sheetH, 0, 0, (float)sheetW, (float)sheetH);

        double scale = Math.Min(1.0, Math.Min(sheetW / imgW, sheetH / imgH));
        double drawW = imgW * scale, drawH = imgH * scale;
        return new PdfPageLayout(
            (float)sheetW, (float)sheetH,
            (float)((sheetW - drawW) / 2), (float)((sheetH - drawH) / 2),
            (float)drawW, (float)drawH);
    }
}
