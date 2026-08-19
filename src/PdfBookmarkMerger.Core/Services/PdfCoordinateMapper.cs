using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PDFユーザー空間(pt単位、左下原点)と、IPdfPageRendererが描画したビットマップのピクセル座標
/// (左上原点)を相互変換する。ビットマップは96*scale DPIで描画されるため、1pt = (96*scale/72)px。
/// </summary>
public static class PdfCoordinateMapper
{
    public static double PixelsPerPoint(float scale) => 96.0 * scale / 72.0;

    public static (double X, double Y) ToPixel(double pdfX, double pdfY, double pageHeightPt, float scale)
    {
        var pixelsPerPoint = PixelsPerPoint(scale);
        return (pdfX * pixelsPerPoint, (pageHeightPt - pdfY) * pixelsPerPoint);
    }

    public static (double X, double Y) ToPdf(double pixelX, double pixelY, double pageHeightPt, float scale)
    {
        var pixelsPerPoint = PixelsPerPoint(scale);
        return (pixelX / pixelsPerPoint, pageHeightPt - (pixelY / pixelsPerPoint));
    }

    public static PdfRect ToPixelRect(PdfRect pdfRect, double pageHeightPt, float scale)
    {
        var (left, top) = ToPixel(pdfRect.Left, pdfRect.Top, pageHeightPt, scale);
        var (right, bottom) = ToPixel(pdfRect.Right, pdfRect.Bottom, pageHeightPt, scale);
        return new PdfRect(Left: left, Bottom: bottom, Right: right, Top: top);
    }
}
