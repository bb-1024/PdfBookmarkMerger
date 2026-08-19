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
    private static (MainWindowViewModel MainVm, FakeMetadataService Metadata, FakeMergeService Merge, FakeBookmarkSettingsExportService Export, FakeDialogService Dialog)
        CreateSut()
    {
        var collector = new FakeFileCollectorService();
        var metadata = new FakeMetadataService();
        var merge = new FakeMergeService();
        var export = new FakeBookmarkSettingsExportService();
        var dialog = new FakeDialogService();
        var userSettings = new FakeUserSettingsService();

        var fileList = new FileListViewModel(collector, metadata, NullLogger<FileListViewModel>.Instance);
        var bookmarkTree = new BookmarkTreeViewModel(dialog);
        var linkEditor = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, new FakePdfLinkAnnotationService(), NullLogger<LinkEditorViewModel>.Instance);

        // 結合(MergeAsync)は成功後に、出力ファイルをLinkEditorViewModel.LoadAsyncで読み直す。
        // FakeDialogService.SaveDialogResultの既定値をここでも登録しておく。
        metadata.RegisterSuccess(dialog.SaveDialogResult!, pageCount: 1);

        var mainVm = new MainWindowViewModel(
            fileList,
            bookmarkTree,
            linkEditor,
            metadata,
            merge,
            export,
            dialog,
            userSettings,
            NullLogger<MainWindowViewModel>.Instance);

        return (mainVm, metadata, merge, export, dialog);
    }

    [Fact]
    public async Task MergeAsync_ExcludesFilesThatFailedMetadataLoad_EvenThoughTheyRemainInFileList()
    {
        var (mainVm, metadata, merge, _, _) = CreateSut();

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
        var (mainVm, metadata, _, _, dialog) = CreateSut();

        var brokenEntry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\broken.pdf" });
        mainVm.FileList.Files.Add(brokenEntry);
        metadata.RegisterFailure(brokenEntry.FilePath);

        await mainVm.ConfirmFilesAsync();

        mainVm.Step.Value.ShouldBe(WorkflowStep.SelectFiles);
        dialog.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SaveBookmarkSettingsAsync_SuggestsXmlExtensionOfMergeDefaultFileName_AndCallsExportService()
    {
        var (mainVm, metadata, _, export, dialog) = CreateSut();

        var entry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\report.pdf" });
        mainVm.FileList.Files.Add(entry);
        metadata.RegisterSuccess(
            entry.FilePath,
            pageCount: 3,
            bookmarks: [new BookmarkNode { SourceFileEntryId = entry.Id, OriginalPageIndex = 0, Title = "A" }]);

        await mainVm.ConfirmFilesAsync();

        dialog.SaveBookmarkSettingsDialogResult = @"C:\out\report_merged.xml";
        await mainVm.SaveBookmarkSettingsAsync();

        export.CallCount.ShouldBe(1);
        export.LastOutputPath.ShouldBe(@"C:\out\report_merged.xml");
        export.LastBookmarks!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SaveBookmarkSettingsAsync_WhenDialogCancelled_DoesNotCallExportService()
    {
        var (mainVm, metadata, _, export, dialog) = CreateSut();

        var entry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\report.pdf" });
        mainVm.FileList.Files.Add(entry);
        metadata.RegisterSuccess(entry.FilePath, pageCount: 3);
        await mainVm.ConfirmFilesAsync();

        dialog.SaveBookmarkSettingsDialogResult = null;
        await mainVm.SaveBookmarkSettingsAsync();

        export.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task EditingPreOffsetPageNumber_DisablesMergeCommand_ButKeepsSaveBookmarkSettingsCommandEnabled()
    {
        var (mainVm, metadata, _, _, _) = CreateSut();

        var entry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\a.pdf" });
        mainVm.FileList.Files.Add(entry);
        metadata.RegisterSuccess(
            entry.FilePath,
            pageCount: 5,
            bookmarks: [new BookmarkNode { SourceFileEntryId = entry.Id, OriginalPageIndex = 0, Title = "A" }]);
        await mainVm.ConfirmFilesAsync();

        mainVm.MergeCommand.CanExecute().ShouldBeTrue();
        mainVm.SaveBookmarkSettingsCommand.CanExecute().ShouldBeTrue();

        var nodeA = mainVm.BookmarkTree.RootNodes.Single(n => n.Title.Value == "A");
        nodeA.PreOffsetPageNumber.Value = 3;

        mainVm.MergeCommand.CanExecute().ShouldBeFalse();
        mainVm.SaveBookmarkSettingsCommand.CanExecute().ShouldBeTrue();

        nodeA.PreOffsetPageNumber.Value = 1;
        mainVm.MergeCommand.CanExecute().ShouldBeTrue();
    }

    [Fact]
    public async Task EditingPreOffsetPageNumberToInvalidValue_DisablesBothMergeAndSaveBookmarkSettingsCommands()
    {
        var (mainVm, metadata, _, _, _) = CreateSut();

        var entry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\a.pdf" });
        mainVm.FileList.Files.Add(entry);
        metadata.RegisterSuccess(
            entry.FilePath,
            pageCount: 5,
            bookmarks: [new BookmarkNode { SourceFileEntryId = entry.Id, OriginalPageIndex = 0, Title = "A" }]);
        await mainVm.ConfirmFilesAsync();

        var nodeA = mainVm.BookmarkTree.RootNodes.Single(n => n.Title.Value == "A");
        nodeA.PreOffsetPageNumber.Value = 0;

        mainVm.MergeCommand.CanExecute().ShouldBeFalse();
        mainVm.SaveBookmarkSettingsCommand.CanExecute().ShouldBeFalse();
    }

    [Fact]
    public async Task BookmarkTreeBusyState_IsForwardedToMainWindowViewModel_AndStatusMessageIsRestoredAfterward()
    {
        // しおりが大量にある状態でBookmarkTree.RecomputeAllPageNumberDisplaysAsyncがバックグラウンドへ
        // 分岐する際、専用のUIを新設せず既存のIsBusy/BusyProgress/処理中オーバーレイをそのまま
        // 再利用できるよう転送していることを確認する回帰テスト。
        var (mainVm, metadata, _, _, _) = CreateSut();

        var entry = new PdfFileEntryViewModel(new PdfFileEntry { FilePath = @"C:\pdfs\a.pdf" });
        mainVm.FileList.Files.Add(entry);
        metadata.RegisterSuccess(entry.FilePath, pageCount: 1);
        await mainVm.ConfirmFilesAsync();

        var originalStatusMessage = mainVm.StatusMessage.Value;

        var busyStates = new List<bool>();
        using var busySub = mainVm.IsBusy.Subscribe(busyStates.Add);
        var progressSnapshots = new List<BusyProgressInfo?>();
        using var progressSub = mainVm.BusyProgress.Subscribe(progressSnapshots.Add);

        await mainVm.BookmarkTree.RecomputeAllPageNumberDisplaysAsync();
        // このヘルパーではノード数がしきい値以下のため実際にはbusyへ遷移しないが、直接IsBusy/
        // BusyProgressを一度trueへ切り替えて転送経路を確認する。
        mainVm.BookmarkTree.IsBusy.Value = true;
        mainVm.BookmarkTree.BusyProgress.Value = new BusyProgressInfo(1, 500, []);
        mainVm.BookmarkTree.IsBusy.Value = false;

        busyStates.ShouldContain(true);
        busyStates[^1].ShouldBeFalse();
        progressSnapshots.ShouldContain(p => p != null && p.CompletedCount == 1 && p.TotalCount == 500);
        mainVm.StatusMessage.Value.ShouldBe(originalStatusMessage, "busy終了後は元のステータスメッセージへ復元されるべき");
    }
}
