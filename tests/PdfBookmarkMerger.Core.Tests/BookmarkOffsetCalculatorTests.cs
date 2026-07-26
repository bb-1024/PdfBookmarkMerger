using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class BookmarkOffsetCalculatorTests
{
    [Fact]
    public void ComputeMergedBookmarks_AddsCumulativePageCountOfPrecedingFiles()
    {
        var fileA = new PdfFileEntry { FilePath = "a.pdf" };
        var fileB = new PdfFileEntry { FilePath = "b.pdf" };

        var nodeA = new BookmarkNode { SourceFileEntryId = fileA.Id, OriginalPageIndex = 0, Title = "A-root" };
        var nodeAChild = new BookmarkNode { SourceFileEntryId = fileA.Id, OriginalPageIndex = 2, Title = "A-child" };
        nodeA.Children.Add(nodeAChild);

        var nodeB = new BookmarkNode { SourceFileEntryId = fileB.Id, OriginalPageIndex = 0, Title = "B-root" };

        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [fileA.Id] = new()
            {
                FileEntryId = fileA.Id,
                PageCount = 5,
                Bookmarks = [nodeA],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
            [fileB.Id] = new()
            {
                FileEntryId = fileB.Id,
                PageCount = 3,
                Bookmarks = [nodeB],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };
        var effectiveBookmarks = new Dictionary<Guid, IReadOnlyList<BookmarkNode>>
        {
            [fileA.Id] = metadataByFileId[fileA.Id].Bookmarks,
            [fileB.Id] = metadataByFileId[fileB.Id].Bookmarks,
        };

        var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks([fileA, fileB], effectiveBookmarks, metadataByFileId);

        merged.Count.ShouldBe(2);

        // fileAは先頭ファイルなのでオフセット0。
        merged[0].Title.ShouldBe("A-root");
        merged[0].MergedPageIndex.ShouldBe(0);
        merged[0].Children[0].MergedPageIndex.ShouldBe(2);

        // fileBはfileA(5ページ)の後に綴じ込まれるためオフセット5。
        merged[1].Title.ShouldBe("B-root");
        merged[1].MergedPageIndex.ShouldBe(5);
    }

    [Fact]
    public void ComputeMergedBookmarks_SkipsFilesWithoutMetadata()
    {
        var fileA = new PdfFileEntry { FilePath = "a.pdf" };
        var fileUnscanned = new PdfFileEntry { FilePath = "unscanned.pdf" };

        var nodeA = new BookmarkNode { SourceFileEntryId = fileA.Id, OriginalPageIndex = 0, Title = "A-root" };
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [fileA.Id] = new()
            {
                FileEntryId = fileA.Id,
                PageCount = 4,
                Bookmarks = [nodeA],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };
        var effectiveBookmarks = new Dictionary<Guid, IReadOnlyList<BookmarkNode>>
        {
            [fileA.Id] = metadataByFileId[fileA.Id].Bookmarks,
        };

        var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks([fileA, fileUnscanned], effectiveBookmarks, metadataByFileId);

        merged.Count.ShouldBe(1);
        merged[0].Title.ShouldBe("A-root");
    }

    [Fact]
    public void ComputeMergedBookmarks_DoesNotMutateInputBookmarkNodes()
    {
        var fileA = new PdfFileEntry { FilePath = "a.pdf" };
        var nodeA = new BookmarkNode { SourceFileEntryId = fileA.Id, OriginalPageIndex = 3, Title = "A-root" };
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [fileA.Id] = new()
            {
                FileEntryId = fileA.Id,
                PageCount = 10,
                Bookmarks = [nodeA],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };
        var effectiveBookmarks = new Dictionary<Guid, IReadOnlyList<BookmarkNode>>
        {
            [fileA.Id] = metadataByFileId[fileA.Id].Bookmarks,
        };

        var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks([fileA], effectiveBookmarks, metadataByFileId);

        // 返り値は複製であり、入力側のnodeA(=metadataByFileId経由でキャッシュされているノード)は
        // MergedPageIndexも含めて一切変更されない。
        nodeA.MergedPageIndex.ShouldBeNull();
        merged[0].ShouldNotBeSameAs(nodeA);
        merged[0].MergedPageIndex.ShouldBe(3);
    }
}
