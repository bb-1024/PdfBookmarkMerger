using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfMergeServiceTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfMetadataService _metadataService = new(NullLogger<PdfMetadataService>.Instance);
    private readonly PdfMergeService _sut = new(NullLogger<PdfMergeService>.Instance);

    public PdfMergeServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public async Task MergeAsync_CombinesMultipleFiles_AppliesOffsetBookmarksAndProperties_EvenWhenOneFileHasNoBookmarks()
    {
        // 3階層以上のしおりを持つサンプルを2つ、しおりを持たないサンプルを1つ用意し、
        // 結合処理がすべて正常に継続されることを検証する。
        var pathA = Path.Combine(_workDirectory, "a.pdf");
        var pathB = Path.Combine(_workDirectory, "b.pdf");
        var pathNoBookmarks = Path.Combine(_workDirectory, "c-no-bookmarks.pdf");

        SamplePdfFactory.CreateWithDeepBookmarks(pathA, pageCount: 6, titlePrefix: "A");
        SamplePdfFactory.CreateWithDeepBookmarks(pathB, pageCount: 4, titlePrefix: "B");
        SamplePdfFactory.CreateWithoutBookmarks(pathNoBookmarks, pageCount: 3);

        var fileA = new PdfFileEntry { FilePath = pathA };
        var fileB = new PdfFileEntry { FilePath = pathB };
        var fileC = new PdfFileEntry { FilePath = pathNoBookmarks };
        var orderedFiles = new List<PdfFileEntry> { fileA, fileB, fileC };

        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>();
        foreach (var file in orderedFiles)
        {
            metadataByFileId[file.Id] = await _metadataService.ReadMetadataAsync(file);
        }

        // このテストの主眼はMissingBookmarkFallbackではなくComputeMergedBookmarks自体の挙動確認のため、
        // 実効しおりは各ファイルのBookmarksをそのまま使う(fileCは0件のまま)。
        var effectiveBookmarks = metadataByFileId.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<BookmarkNode>)kv.Value.Bookmarks);
        var mergedBookmarks = BookmarkOffsetCalculator.ComputeMergedBookmarks(orderedFiles, effectiveBookmarks, metadataByFileId);

        var outputPath = Path.Combine(_workDirectory, "merged.pdf");
        var properties = new PdfDocumentPropertiesModel
        {
            Title = "結合テスト",
            Author = "テスト太郎",
            Subject = "件名",
            Keywords = "kw1,kw2",
            Creator = "PdfBookmarkMerger.Core.Tests",
        };

        var request = new PdfMergeRequest
        {
            Files = orderedFiles,
            Bookmarks = mergedBookmarks,
            Properties = properties,
            OutputPath = outputPath,
        };

        await _sut.MergeAsync(request);

        File.Exists(outputPath).ShouldBeTrue();

        using var merged = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);

        // 6 + 4 + 3 = 13ページに結合されていること。
        merged.PageCount.ShouldBe(13);

        // プロパティが反映されていること(先頭ファイルのプロパティを流用しつつユーザー編集を適用)。
        merged.Info.Title.ShouldBe("結合テスト");
        merged.Info.Author.ShouldBe("テスト太郎");

        // しおりを持たないfileCは何もルートしおりを追加しないため、ルートしおりはfileA由来+fileB由来の2件のみ。
        merged.Outlines.Count.ShouldBe(2);

        var partA = merged.Outlines[0];
        partA.Title.ShouldBe("A Part 1");
        FindPageIndex(merged, partA.DestinationPage).ShouldBe(0);
        partA.Outlines.Count.ShouldBe(2);

        var chapterA1 = partA.Outlines[0];
        chapterA1.Title.ShouldBe("A Chapter 1");
        FindPageIndex(merged, chapterA1.DestinationPage).ShouldBe(1);

        var sectionA = chapterA1.Outlines[0];
        FindPageIndex(merged, sectionA.DestinationPage).ShouldBe(2);

        var subsectionA = sectionA.Outlines[0];
        FindPageIndex(merged, subsectionA.DestinationPage).ShouldBe(3);

        // fileB(4ページ)はfileA(6ページ)の後に綴じ込まれるため、オフセットは6。
        var partB = merged.Outlines[1];
        partB.Title.ShouldBe("B Part 1");
        FindPageIndex(merged, partB.DestinationPage).ShouldBe(6 + 0);

        var chapterB1 = partB.Outlines[0];
        FindPageIndex(merged, chapterB1.DestinationPage).ShouldBe(6 + 1);
    }

    [Fact]
    public async Task MergeAsync_PreservesOpenedState_ForNestedBookmarkUnderClosedParent()
    {
        // 親が非展開(false)・子が展開(true)のしおりを持つソースPDFを用意し、
        // 結合後も子の展開状態が維持されることを検証する(PDFsharpの/Count書き込み不具合の回避確認)。
        var sourcePath = Path.Combine(_workDirectory, "nested-opened.pdf");
        using (var document = new PdfDocument())
        {
            var p0 = document.AddPage();
            var p1 = document.AddPage();
            var p2 = document.AddPage();

            var part = document.Outlines.Add("Part", p0, false);
            var chapter = part.Outlines.Add("Chapter", p1, true);
            chapter.Outlines.Add("Leaf", p2, false);

            // ソースPDF自体もPDFsharpで生成するため、フィクスチャ作成時点で同じ書き込み不具合の
            // 影響を受けないよう、ここでも/Countを明示的に補正しておく。
            part.Elements.SetInteger("/Count", -1);
            chapter.Elements.SetInteger("/Count", 1);

            document.Save(sourcePath);
        }

        var file = new PdfFileEntry { FilePath = sourcePath };
        var metadata = await _metadataService.ReadMetadataAsync(file);

        var partNode = metadata.Bookmarks.ShouldHaveSingleItem();
        partNode.IsOpen.ShouldBeFalse();
        var chapterNode = partNode.Children.ShouldHaveSingleItem();
        chapterNode.IsOpen.ShouldBeTrue();

        var outputPath = Path.Combine(_workDirectory, "nested-opened-merged.pdf");
        var request = new PdfMergeRequest
        {
            Files = [file],
            Bookmarks = metadata.Bookmarks,
            Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            OutputPath = outputPath,
        };

        await _sut.MergeAsync(request);

        using var merged = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        var mergedPart = merged.Outlines[0];
        var mergedChapter = mergedPart.Outlines[0];

        mergedPart.Elements.GetInteger("/Count").ShouldBeLessThan(0);
        mergedChapter.Elements.ContainsKey("/Count").ShouldBeTrue();
        mergedChapter.Elements.GetInteger("/Count").ShouldBeGreaterThan(0);
    }

    private static int FindPageIndex(PdfDocument document, PdfPage? page)
    {
        page.ShouldNotBeNull();
        for (var i = 0; i < document.Pages.Count; i++)
        {
            if (ReferenceEquals(document.Pages[i], page))
            {
                return i;
            }
        }

        throw new InvalidOperationException("ページが結合結果内に見つかりません。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
