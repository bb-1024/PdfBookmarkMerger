using PdfSharp.Pdf;

namespace PdfBookmarkMerger.Core.Tests.TestHelpers;

/// <summary>
/// テスト用サンプルPDFを生成するヘルパー。
/// 3階層以上のしおりを持つPDFと、しおりを持たないPDFの両方を作成できる。
/// </summary>
internal static class SamplePdfFactory
{
    /// <summary>
    /// pageCountページから成り、Part &gt; Chapter &gt; Section &gt; Subsectionの4階層のしおりを持つPDFを作成する。
    /// </summary>
    public static void CreateWithDeepBookmarks(string filePath, int pageCount, string titlePrefix)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        PdfPage PageAt(int index) => document.Pages[Math.Min(index, pageCount - 1)];

        var part = document.Outlines.Add($"{titlePrefix} Part 1", PageAt(0), true);
        var chapter1 = part.Outlines.Add($"{titlePrefix} Chapter 1", PageAt(1), true);
        var section = chapter1.Outlines.Add($"{titlePrefix} Section 1.1", PageAt(2), false);
        section.Outlines.Add($"{titlePrefix} Subsection 1.1.1", PageAt(3), false);
        part.Outlines.Add($"{titlePrefix} Chapter 2", PageAt(4), false);

        document.Save(filePath);
    }

    /// <summary>しおりを一切持たないpageCountページのPDFを作成する。</summary>
    public static void CreateWithoutBookmarks(string filePath, int pageCount)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        document.Save(filePath);
    }

    /// <summary>
    /// 1ページ・指定テキストのみを持つPDFを作成する。テキスト抽出・描画系のテストで使う。
    /// PDFsharpのXFont経由だとフォントリゾルバ(GlobalFontSettings.FontResolver)の設定が
    /// テスト側でも必要になるため、標準14フォント(Helvetica)を使う最小限のPDFを直接組み立てる。
    /// </summary>
    public static void CreateWithText(string filePath, string text, double pageWidth = 612, double pageHeight = 792)
    {
        var content = $"BT /F1 24 Tf 72 700 Td ({EscapePdfString(text)}) Tj ET";
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(content);

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream",
        };

        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new StreamWriter(fs, System.Text.Encoding.ASCII);

        writer.Write("%PDF-1.4\n");
        writer.Flush();

        var offsets = new long[objects.Length + 1];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = fs.Position;
            writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            writer.Flush();
        }

        var xrefStart = fs.Position;
        writer.Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        for (var i = 1; i <= objects.Length; i++)
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }

        writer.Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");
        writer.Flush();
    }

    private static string EscapePdfString(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
