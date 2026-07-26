using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// ConfirmFilesAsyncでメタデータ読み込みに失敗したファイルが、しおりツリーだけでなく
/// 実際の結合(MergeAsync)の対象からも一貫して除外されることを検証する回帰テスト。
/// (かつては、しおりツリーからは除外されるが結合対象には残ってしまい、結合失敗や
/// ページオフセットのずれを引き起こすバグがあった)
/// </summary>
public sealed class MainWindowViewModelTests
{
    private static (MainWindowViewModel MainVm, FakeMetadataService Metadata, FakeMergeService Merge, FakeDialogService Dialog)
        CreateSut()
    {
        var collector = new FakeFileCollectorService();
        var metadata = new FakeMetadataService();
        var merge = new FakeMergeService();
        var dialog = new FakeDialogService();
        var userSettings = new FakeUserSettingsService();

        var fileList = new FileListViewModel(collector, metadata, NullLogger<FileListViewModel>.Instance);
        var bookmarkTree = new BookmarkTreeViewModel(dialog);

        var mainVm = new MainWindowViewModel(
            fileList,
            bookmarkTree,
            metadata,
            merge,
            dialog,
            userSettings,
            NullLogger<MainWindowViewModel>.Instance);

        return (mainVm, metadata, merge, dialog);
    }

    [Fact]
    public async Task MergeAsync_ExcludesFilesThatFailedMetadataLoad_EvenThoughTheyRemainInFileList()
    {
        var (mainVm, metadata, merge, _) = CreateSut();

        var goodEntry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\a.pdf" });
        var brokenEntry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\b-broken.pdf" });
        mainVm.FileList.Files.Add(goodEntry);
        mainVm.FileList.Files.Add(brokenEntry);

        metadata.RegisterSuccess(
            goodEntry.FilePath,
            pageCount: 3,
            bookmarks: [new BookmarkNode { SourceFileEntryId = goodEntry.Id, OriginalPageIndex = 0, Title = "A" }]);
        metadata.RegisterFailure(brokenEntry.FilePath);

        await mainVm.ConfirmFilesAsync();

        // 失敗したファイルにはフラグが立つが、一覧そのものからは削除されない。
        goodEntry.LoadFailed.Value.ShouldBeFalse();
        brokenEntry.LoadFailed.Value.ShouldBeTrue();
        mainVm.FileList.Files.Count.ShouldBe(2);

        // しおりツリーには成功した1ファイル分のみが反映される。
        mainVm.Step.Value.ShouldBe(WorkflowStep.EditBookmarks);
        mainVm.BookmarkTree.RootNodes.Count.ShouldBe(1);

        await mainVm.MergeAsync();

        // 結合リクエストにも、失敗したファイルが混入していないこと。
        merge.CallCount.ShouldBe(1);
        merge.LastRequest.ShouldNotBeNull();
        merge.LastRequest!.Files.Count.ShouldBe(1);
        merge.LastRequest.Files[0].FilePath.ShouldBe(goodEntry.FilePath);
    }

    [Fact]
    public async Task ConfirmFilesAsync_WhenAllFilesFail_DoesNotAdvanceToEditBookmarksStep()
    {
        var (mainVm, metadata, _, dialog) = CreateSut();

        var brokenEntry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\broken.pdf" });
        mainVm.FileList.Files.Add(brokenEntry);
        metadata.RegisterFailure(brokenEntry.FilePath);

        await mainVm.ConfirmFilesAsync();

        mainVm.Step.Value.ShouldBe(WorkflowStep.SelectFiles);
        dialog.Errors.ShouldNotBeEmpty();
    }
}
