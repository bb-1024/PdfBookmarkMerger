using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfCoordinateMapperTests
{
    [Fact]
    public void ToPixel_OriginAtBottomLeft_MapsToBottomOfPixelSpace()
    {
        var (x, y) = PdfCoordinateMapper.ToPixel(0, 0, pageHeightPt: 792, scale: 1.0f);

        x.ShouldBe(0, tolerance: 0.01);
        // PDF原点(左下)は、ページ全体の高さ分だけピクセル空間の下側(Y軸最大値)に対応する。
        y.ShouldBe(792 * PdfCoordinateMapper.PixelsPerPoint(1.0f), tolerance: 0.01);
    }

    [Fact]
    public void ToPixel_TopOfPage_MapsToPixelYZero()
    {
        var (_, y) = PdfCoordinateMapper.ToPixel(0, 792, pageHeightPt: 792, scale: 1.0f);

        y.ShouldBe(0, tolerance: 0.01);
    }

    [Fact]
    public void ToPixel_AndToPdf_AreInverses()
    {
        const double pageHeight = 842;
        const float scale = 1.75f;

        var (pixelX, pixelY) = PdfCoordinateMapper.ToPixel(123.4, 567.8, pageHeight, scale);
        var (pdfX, pdfY) = PdfCoordinateMapper.ToPdf(pixelX, pixelY, pageHeight, scale);

        pdfX.ShouldBe(123.4, tolerance: 0.001);
        pdfY.ShouldBe(567.8, tolerance: 0.001);
    }

    [Fact]
    public void PixelsPerPoint_DoublesWhenScaleDoubles()
    {
        var scale1 = PdfCoordinateMapper.PixelsPerPoint(1.0f);
        var scale2 = PdfCoordinateMapper.PixelsPerPoint(2.0f);

        scale2.ShouldBe(scale1 * 2, tolerance: 0.0001);
    }

    [Fact]
    public void ToPixelRect_ConvertsAllFourCorners_KeepingLeftLessThanRightAndTopLessThanBottom()
    {
        var pdfRect = new PdfRect(Left: 50, Bottom: 100, Right: 150, Top: 120);

        var pixelRect = PdfCoordinateMapper.ToPixelRect(pdfRect, pageHeightPt: 792, scale: 1.0f);

        pixelRect.Left.ShouldBeLessThan(pixelRect.Right);
        // PDF側はTop(120) > Bottom(100)だが、ピクセル空間は上下反転するためTop(ピクセルY)の方が小さくなる。
        pixelRect.Top.ShouldBeLessThan(pixelRect.Bottom);
    }
}
