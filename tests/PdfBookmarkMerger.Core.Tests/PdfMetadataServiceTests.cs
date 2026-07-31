using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfMetadataServiceTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfMetadataService _sut = new(NullLogger<PdfMetadataService>.Instance);

    public PdfMetadataServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public async Task ReadMetadataAsync_ExtractsPageCountAndDeepBookmarkTree()
    {
        var filePath = Path.Combine(_workDirectory, "deep.pdf");
        SamplePdfFactory.CreateWithDeepBookmarks(filePath, pageCount: 6, titlePrefix: "A");
        var file = new PdfFileEntry { FilePath = filePath };

        var metadata = await _sut.ReadMetadataAsync(file);

        metadata.PageCount.ShouldBe(6);
        metadata.Bookmarks.Count.ShouldBe(1);

        var part = metadata.Bookmarks[0];
        part.Title.ShouldBe("A Part 1");
        part.OriginalPageIndex.ShouldBe(0);
        // 注: PDFsharp 6.2.4は独自出力したOutlineの/Count(開閉状態)の読み戻しに既知の制限があるため、
        // Openedの厳密な値検証はここでは行わない。IsOpenフィールド自体はモデルに保持・往復させている
        // (PdfMergeServiceTests / 設計ドキュメントの既知の制限事項を参照)。
        part.SourceFileEntryId.ShouldBe(file.Id);
        part.Children.Count.ShouldBe(2);

        var chapter1 = part.Children[0];
        chapter1.Title.ShouldBe("A Chapter 1");
        chapter1.OriginalPageIndex.ShouldBe(1);
        chapter1.Children.Count.ShouldBe(1);

        var section = chapter1.Children[0];
        section.Title.ShouldBe("A Section 1.1");
        section.OriginalPageIndex.ShouldBe(2);
        section.Children.Count.ShouldBe(1);

        var subsection = section.Children[0];
        subsection.Title.ShouldBe("A Subsection 1.1.1");
        subsection.OriginalPageIndex.ShouldBe(3);
        subsection.Children.ShouldBeEmpty();

        var chapter2 = part.Children[1];
        chapter2.Title.ShouldBe("A Chapter 2");
        chapter2.OriginalPageIndex.ShouldBe(4);
    }

    [Fact]
    public async Task ReadMetadataAsync_NeverProducesNonFiniteDestinationCoordinates()
    {
        // PDFsharpのPdfOutline.Left/Top/Right/Bottom/Zoomは、宛先タイプ(/FitH等)によって
        // 該当項目が存在しない場合にNaNを返す。これをそのままBookmarkNodeへ保持すると、Undo履歴の
        // json化(PushUndoSnapshotCore)がArgumentExceptionで失敗し、D&D・レベル変更等の編集操作が
        // 実質フリーズしたように見える不具合が起きていた(実際は未処理例外)。ReadMetadataAsyncの
        // 出力にNaN/Infinityが含まれないことを恒久的に検証する。
        var filePath = Path.Combine(_workDirectory, "deep.pdf");
        SamplePdfFactory.CreateWithDeepBookmarks(filePath, pageCount: 6, titlePrefix: "A");
        var file = new PdfFileEntry { FilePath = filePath };

        var metadata = await _sut.ReadMetadataAsync(file);

        void AssertFinite(IEnumerable<BookmarkNode> nodes)
        {
            foreach (var node in nodes)
            {
                foreach (var value in new[] { node.Left, node.Top, node.Right, node.Bottom, node.Zoom })
                {
                    if (value is { } d)
                    {
                        double.IsFinite(d).ShouldBeTrue($"{node.Title} に非有限値が設定されている: {d}");
                    }
                }

                AssertFinite(node.Children);
            }
        }

        AssertFinite(metadata.Bookmarks);
    }

    [Fact]
    public async Task ReadMetadataAsync_WithNoBookmarks_ReturnsEmptyBookmarkListAndContinuesNormally()
    {
        var filePath = Path.Combine(_workDirectory, "no-bookmarks.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(filePath, pageCount: 3);
        var file = new PdfFileEntry { FilePath = filePath };

        var metadata = await _sut.ReadMetadataAsync(file);

        metadata.PageCount.ShouldBe(3);
        metadata.Bookmarks.ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
