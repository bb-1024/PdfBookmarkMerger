using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfBookmarkMerger.SampleGenerator;

/// <summary>
/// サンプルPDF生成で共通利用するレイアウト定数・フォント・描画/しおり作成ヘルパー。
/// 各定数値(804/444/24など)は、元のsample-a/b-deep-bookmarks.pdfをPDFsharpで読み戻して
/// 実測した値に合わせている。
/// </summary>
internal static class SampleGeneratorConstants
{
    public const string Author = "PdfBookmarkMerger Sample Generator";
    public const string Creator = "PDFsharp 6.2.4 (www.pdfsharp.com)";

    public const double PageWidth = 595;
    public const double PageHeight = 842;
    public const double LeftMargin = 50;

    public const double HeaderTopY = 60;
    public const double SecondBlockTopY = 420;
    public const double LabelOffsetY = 18;
    public const double FooterTopY = 806;
    public const double FillerNoteTopY = 100;
    public const double PlainTextTopY = 56;

    /// <summary>しおりのXYZ Topは、対応する見出しの描画開始Y(下から数えた座標)より24pt上を指す。</summary>
    public const double BookmarkTopMargin = 24;

    private static readonly string[] LevelNames = ["Part", "Chapter", "Section", "Subsection"];

    public static readonly XFont HeaderFont = new("Arial", 13, XFontStyleEx.Bold);
    public static readonly XFont LabelFont = new("Arial", 10, XFontStyleEx.Regular);
    public static readonly XFont FooterFont = new("Arial", 9, XFontStyleEx.Italic);
    public static readonly XFont PlainFont = new("Arial", 11, XFontStyleEx.Regular);

    public static readonly XBrush BlackBrush = new XSolidBrush(XColor.FromArgb(0, 0, 0));
    public static readonly XBrush DimGrayBrush = new XSolidBrush(XColor.FromArgb(105, 105, 105));
    public static readonly XBrush GrayBrush = new XSolidBrush(XColor.FromArgb(128, 128, 128));

    public static PdfPage NewPage(PdfDocument doc)
    {
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(PageWidth);
        page.Height = XUnit.FromPoint(PageHeight);
        return page;
    }

    public static void DrawHeaderBlock(XGraphics gfx, double topY, string title, int level)
    {
        gfx.DrawString(title, HeaderFont, BlackBrush, new XPoint(LeftMargin, topY), XStringFormats.TopLeft);
        gfx.DrawString(
            $"Bookmark level: {level} ({LevelNames[level - 1]})",
            LabelFont,
            DimGrayBrush,
            new XPoint(LeftMargin, topY + LabelOffsetY),
            XStringFormats.TopLeft);
    }

    public static void DrawFooter(XGraphics gfx, string letter, int pageNumber, int totalPages) =>
        gfx.DrawString(
            $"{letter} sample  -  page {pageNumber} / {totalPages}",
            FooterFont,
            GrayBrush,
            new XPoint(LeftMargin, FooterTopY),
            XStringFormats.TopLeft);

    public static void DrawFillerNote(XGraphics gfx, string referenceTitle, string letter, int pageNumber, int totalPages)
    {
        gfx.DrawString(
            $"({referenceTitle} continued - not a direct bookmark target)",
            FooterFont,
            GrayBrush,
            new XPoint(LeftMargin, FillerNoteTopY),
            XStringFormats.TopLeft);
        DrawFooter(gfx, letter, pageNumber, totalPages);
    }

    public static PdfOutline AddBookmark(PdfOutlineCollection parent, string title, PdfPage page, double top)
    {
        var outline = parent.Add(title, page, false);
        outline.PageDestinationType = PdfPageDestinationType.Xyz;
        outline.Left = 0;
        outline.Top = top;
        return outline;
    }

    /// <summary>
    /// PDFsharp 6.2.4は、document.Outlinesの直下(第1階層)以外のしおりについて
    /// 開閉状態を表す/Countを書き込まない既知の不具合がある。保存前に直接設定して回避する
    /// (src/PdfBookmarkMerger.Core/Services/PdfMergeService.csと同じ対処)。
    /// </summary>
    public static void FixCount(PdfOutline outline, int childCount) =>
        outline.Elements.SetInteger("/Count", -childCount);
}
