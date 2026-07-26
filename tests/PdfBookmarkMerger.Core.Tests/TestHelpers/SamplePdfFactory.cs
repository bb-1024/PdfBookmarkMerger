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
}
