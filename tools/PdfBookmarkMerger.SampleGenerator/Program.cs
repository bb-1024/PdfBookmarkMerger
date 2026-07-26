// tests/sample 配下の手動テスト用サンプルPDF(sample-a-deep-bookmarks.pdf / sample-b-deep-bookmarks.pdf /
// sample-c-no-bookmarks.pdf)を再現するジェネレーター。
// 実行方法: dotnet run --project tools/PdfBookmarkMerger.SampleGenerator [出力先ディレクトリ]
// 出力先省略時は既定でリポジトリの tests/sample を対象とする。
using PdfBookmarkMerger.SampleGenerator;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

GlobalFontSettings.FontResolver = new ArialFontResolver();

var outputDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "sample"));
Directory.CreateDirectory(outputDir);

GenerateDeepBookmarkSample(Path.Combine(outputDir, "sample-a-deep-bookmarks.pdf"), "A", fillerPagesBeforeSection13: 2);
GenerateDeepBookmarkSample(Path.Combine(outputDir, "sample-b-deep-bookmarks.pdf"), "B", fillerPagesBeforeSection13: 3);
GenerateNoBookmarkSample(Path.Combine(outputDir, "sample-c-no-bookmarks.pdf"));

Console.WriteLine($"生成完了: {outputDir}");

static void GenerateDeepBookmarkSample(string outputPath, string letter, int fillerPagesBeforeSection13)
{
    var totalPages = 11 + fillerPagesBeforeSection13;

    using var doc = new PdfDocument();
    doc.Info.Title = $"{letter} sample (deep bookmarks, mixed layouts)";
    doc.Info.Author = SampleGeneratorConstants.Author;
    doc.Info.Creator = SampleGeneratorConstants.Creator;

    var pageNumber = 0;
    PdfPage NextPage()
    {
        pageNumber++;
        return SampleGeneratorConstants.NewPage(doc);
    }

    var part1Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(part1Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Part 1", 1);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var part1 = SampleGeneratorConstants.AddBookmark(doc.Outlines, $"{letter} Part 1", part1Page, 804);

    var chapter1Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(chapter1Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Chapter 1", 2);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var chapter1 = SampleGeneratorConstants.AddBookmark(part1.Outlines, $"{letter} Chapter 1", chapter1Page, 804);

    // Section 1.1 と Subsection 1.1.1 は同一ページを共有し、Topの異なる2つのしおりが1ページ内の別位置を指す
    // (「混在レイアウト」= 同一ページ内で複数のしおりが異なるスクロール位置を指すケースの再現)。
    var sharedPage1 = NextPage();
    using (var gfx = XGraphics.FromPdfPage(sharedPage1))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Section 1.1", 3);
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.SecondBlockTopY, $"{letter} Subsection 1.1.1", 4);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var section11 = SampleGeneratorConstants.AddBookmark(chapter1.Outlines, $"{letter} Section 1.1", sharedPage1, 804);
    SampleGeneratorConstants.AddBookmark(section11.Outlines, $"{letter} Subsection 1.1.1", sharedPage1, 444);
    SampleGeneratorConstants.FixCount(section11, 1);

    var section12Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(section12Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Section 1.2", 3);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var section12 = SampleGeneratorConstants.AddBookmark(chapter1.Outlines, $"{letter} Section 1.2", section12Page, 804);

    var subsection121Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(subsection121Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Subsection 1.2.1", 4);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    SampleGeneratorConstants.AddBookmark(section12.Outlines, $"{letter} Subsection 1.2.1", subsection121Page, 804);
    SampleGeneratorConstants.FixCount(section12, 1);

    // しおりが直接指さない「繋ぎ」ページ(直前のしおりの続き、という体裁)。
    for (var i = 0; i < fillerPagesBeforeSection13; i++)
    {
        var fillerPage = NextPage();
        using var gfx = XGraphics.FromPdfPage(fillerPage);
        SampleGeneratorConstants.DrawFillerNote(gfx, $"{letter} Subsection 1.2.1", letter, pageNumber, totalPages);
    }

    var section13Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(section13Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Section 1.3", 3);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    SampleGeneratorConstants.AddBookmark(chapter1.Outlines, $"{letter} Section 1.3", section13Page, 804);
    SampleGeneratorConstants.FixCount(chapter1, 3);

    var chapter2Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(chapter2Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Chapter 2", 2);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var chapter2 = SampleGeneratorConstants.AddBookmark(part1.Outlines, $"{letter} Chapter 2", chapter2Page, 804);

    var section21Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(section21Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Section 2.1", 3);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    SampleGeneratorConstants.AddBookmark(chapter2.Outlines, $"{letter} Section 2.1", section21Page, 804);
    SampleGeneratorConstants.FixCount(chapter2, 1);

    // Part1直下(第1階層)は/Countの既知不具合の対象外のため補正不要。

    var part2Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(part2Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Part 2", 1);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var part2 = SampleGeneratorConstants.AddBookmark(doc.Outlines, $"{letter} Part 2", part2Page, 804);

    var chapter3Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(chapter3Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Chapter 3", 2);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    var chapter3 = SampleGeneratorConstants.AddBookmark(part2.Outlines, $"{letter} Chapter 3", chapter3Page, 804);

    var section31Page = NextPage();
    using (var gfx = XGraphics.FromPdfPage(section31Page))
    {
        SampleGeneratorConstants.DrawHeaderBlock(gfx, SampleGeneratorConstants.HeaderTopY, $"{letter} Section 3.1", 3);
        SampleGeneratorConstants.DrawFooter(gfx, letter, pageNumber, totalPages);
    }
    SampleGeneratorConstants.AddBookmark(chapter3.Outlines, $"{letter} Section 3.1", section31Page, 804);
    SampleGeneratorConstants.FixCount(chapter3, 1);

    doc.Save(outputPath);
    Console.WriteLine($"generated: {outputPath}");
}

static void GenerateNoBookmarkSample(string outputPath)
{
    const int totalPages = 3;

    using var doc = new PdfDocument();
    doc.Info.Title = "Sample (no bookmarks)";
    doc.Info.Author = SampleGeneratorConstants.Author;
    doc.Info.Creator = SampleGeneratorConstants.Creator;

    for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
    {
        var page = SampleGeneratorConstants.NewPage(doc);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString(
            $"No-bookmark sample - page {pageNumber} / {totalPages}",
            SampleGeneratorConstants.PlainFont,
            SampleGeneratorConstants.BlackBrush,
            new XPoint(SampleGeneratorConstants.LeftMargin, SampleGeneratorConstants.PlainTextTopY),
            XStringFormats.TopLeft);
    }

    doc.Save(outputPath);
    Console.WriteLine($"generated: {outputPath}");
}
