using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class MissingBookmarkFallbackTests
{
    [Fact]
    public void ResolveEffectiveBookmarks_AddsFileNameTitledBookmark_ForFileWithoutBookmarks()
    {
        var file = new PdfFileEntry { FilePath = @"C:\docs\Annual Report 2026.pdf" };
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [file.Id] = new()
            {
                FileEntryId = file.Id,
                PageCount = 5,
                Bookmarks = [],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };

        var effective = MissingBookmarkFallback.ResolveEffectiveBookmarks([file], metadataByFileId);

        var bookmarks = effective[file.Id];
        bookmarks.Count.ShouldBe(1);
        bookmarks[0].Title.ShouldBe("Annual Report 2026");
        bookmarks[0].SourceFileEntryId.ShouldBe(file.Id);
        bookmarks[0].OriginalPageIndex.ShouldBe(0);

        // 元のmetadataByFileIdは変更されない(非破壊)。
        metadataByFileId[file.Id].Bookmarks.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveEffectiveBookmarks_CopiesDestinationTypeFromPreviousFile_ButNotCoordinates()
    {
        var fileA = new PdfFileEntry { FilePath = "a.pdf" };
        var fileB = new PdfFileEntry { FilePath = "b-no-bookmarks.pdf" };

        var nodeA = new BookmarkNode
        {
            SourceFileEntryId = fileA.Id,
            OriginalPageIndex = 2,
            Title = "A-root",
            DestinationType = BookmarkDestinationType.XYZ,
            Left = 12,
            Top = 34,
            Zoom = 2,
        };

        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [fileA.Id] = new()
            {
                FileEntryId = fileA.Id,
                PageCount = 3,
                Bookmarks = [nodeA],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
            [fileB.Id] = new()
            {
                FileEntryId = fileB.Id,
                PageCount = 2,
                Bookmarks = [],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };

        var effective = MissingBookmarkFallback.ResolveEffectiveBookmarks([fileA, fileB], metadataByFileId);

        var autoNode = effective[fileB.Id].ShouldHaveSingleItem();
        autoNode.DestinationType.ShouldBe(BookmarkDestinationType.XYZ);

        // 座標(表示位置)は引き継がない。
        autoNode.Left.ShouldBeNull();
        autoNode.Top.ShouldBeNull();
        autoNode.Zoom.ShouldBeNull();
    }

    [Fact]
    public void ResolveEffectiveBookmarks_UsesDefaultDestinationType_WhenNoPreviousFileExists()
    {
        var file = new PdfFileEntry { FilePath = "first.pdf" };
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [file.Id] = new()
            {
                FileEntryId = file.Id,
                PageCount = 1,
                Bookmarks = [],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };

        var effective = MissingBookmarkFallback.ResolveEffectiveBookmarks([file], metadataByFileId);

        effective[file.Id].ShouldHaveSingleItem().DestinationType.ShouldBe(BookmarkDestinationType.Fit);
    }

    [Fact]
    public void ResolveEffectiveBookmarks_ReturnsSameInstances_ForFileThatAlreadyHasBookmarks()
    {
        var file = new PdfFileEntry { FilePath = "has-bookmarks.pdf" };
        var existing = new BookmarkNode { SourceFileEntryId = file.Id, OriginalPageIndex = 0, Title = "既存のしおり" };
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>
        {
            [file.Id] = new()
            {
                FileEntryId = file.Id,
                PageCount = 1,
                Bookmarks = [existing],
                Properties = PdfDocumentPropertiesModel.CreateEmpty(),
            },
        };

        var effective = MissingBookmarkFallback.ResolveEffectiveBookmarks([file], metadataByFileId);

        effective[file.Id].ShouldHaveSingleItem().ShouldBeSameAs(existing);
    }
}
