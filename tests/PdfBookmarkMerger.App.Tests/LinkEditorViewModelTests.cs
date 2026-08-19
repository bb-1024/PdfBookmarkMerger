using System.Windows.Input;
using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

public sealed class LinkEditorViewModelTests
{
    private static (LinkEditorViewModel Vm, FakeMetadataService Metadata, FakePdfTextExtractor TextExtractor, FakePdfLinkAnnotationService LinkAnnotationService) CreateSut()
    {
        var metadata = new FakeMetadataService();
        var textExtractor = new FakePdfTextExtractor();
        var linkAnnotationService = new FakePdfLinkAnnotationService();
        var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), textExtractor, metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);
        return (vm, metadata, textExtractor, linkAnnotationService);
    }

    private static async Task WaitUntilIdleAsync(LinkEditorViewModel vm)
    {
        for (var i = 0; i < 100 && vm.IsBusy.Value; i++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// PageSlotsの遅延描画・ズーム時の再描画はIsBusyを介さない(バックグラウンドでの読み込みで
    /// 全体をロックしないため)ので、WaitUntilIdleAsyncではなくこちらで完了を待つ。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task LoadAsync_PopulatesPageCountAndBookmarksAndPageSlots()
    {
        var (vm, metadata, _, _) = CreateSut();
        var bookmark = new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 2, Title = "Chapter 1" };
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 5, bookmarks: [bookmark]);

        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.FilePath.Value.ShouldBe(@"C:\out\merged.pdf");
        vm.PageCount.Value.ShouldBe(5);
        vm.CurrentPageIndex.Value.ShouldBe(0);
        vm.PageNumberInput.Value.ShouldBe(1);
        vm.Bookmarks.Value.ShouldHaveSingleItem().Title.ShouldBe("Chapter 1");

        // 連続スクロール表示用に全ページ分のプレースホルダが用意されるが、まだどのページも
        // ビューポートに入っていない(LoadPageSlotAsyncが呼ばれていない)ため画像は未描画。
        vm.PageSlots.Count.ShouldBe(5);
        vm.PageSlots.Select(s => s.PageIndex).ShouldBe([0, 1, 2, 3, 4]);
        vm.PageSlots.ShouldAllBe(s => s.Image.Value == null);
        vm.PageSlots[0].IsCurrent.Value.ShouldBeTrue();
        vm.PageSlots[1].IsCurrent.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadPageSlotAsync_RendersTheRequestedSlot_AndUnloadPageSlot_ClearsItsImage()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.PageSlots[1].Image.Value.ShouldBeNull();

        await vm.LoadPageSlotAsync(1);

        vm.PageSlots[1].Image.Value.ShouldNotBeNull();
        // 他のスロットには影響しない。
        vm.PageSlots[0].Image.Value.ShouldBeNull();
        vm.PageSlots[2].Image.Value.ShouldBeNull();

        vm.UnloadPageSlot(1);

        vm.PageSlots[1].Image.Value.ShouldBeNull();
    }

    [Fact]
    public async Task LoadPageSlotAsync_OutOfRangePageIndex_DoesNothing()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 2);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        await vm.LoadPageSlotAsync(-1);
        await vm.LoadPageSlotAsync(2);

        vm.PageSlots.ShouldAllBe(s => s.Image.Value == null);
    }

    [Fact]
    public async Task PageNumberInput_And_CurrentPageIndex_StaySynchronized_WithClamping()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 5);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        // テキストボックスへ入力 → CurrentPageIndexへ反映(1始まり→0始まり)。
        vm.PageNumberInput.Value = 3;
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(2);

        // ページ送りボタン等でCurrentPageIndexが変わった場合もテキストボックスへ反映される。
        vm.NextPageCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.PageNumberInput.Value.ShouldBe(4);

        // 範囲外の入力は最も近い有効な値へ丸められる。
        vm.PageNumberInput.Value = 999;
        await WaitUntilIdleAsync(vm);
        vm.PageNumberInput.Value.ShouldBe(5);
        vm.CurrentPageIndex.Value.ShouldBe(4);

        vm.PageNumberInput.Value = 0;
        await WaitUntilIdleAsync(vm);
        vm.PageNumberInput.Value.ShouldBe(1);
        vm.CurrentPageIndex.Value.ShouldBe(0);
    }

    [Fact]
    public async Task ZoomScaleChange_ReRendersOnlyCurrentlyVisibleSlots_AndClearsHiddenOnes()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        // ページ0(LoadAsync時にCurrentPageIndexの変更として自動でメタデータ取得のみ行われるが、
        // 画像描画はまだ)とページ1を「ビューポートに入った」ものとして明示的にロードする。
        await vm.LoadPageSlotAsync(0);
        await vm.LoadPageSlotAsync(1);
        vm.PageSlots[0].Image.Value.ShouldNotBeNull();
        vm.PageSlots[1].Image.Value.ShouldNotBeNull();

        var placeholderWidthBeforeZoom = vm.PlaceholderWidth.Value;

        vm.ZoomInCommand.Execute();
        await WaitUntilAsync(() => vm.PlaceholderWidth.Value != placeholderWidthBeforeZoom);
        await WaitUntilAsync(() => vm.PageSlots[0].Image.Value is not null && vm.PageSlots[1].Image.Value is not null);

        // プレースホルダサイズがズームに応じて再計算される。
        vm.PlaceholderWidth.Value.ShouldBeGreaterThan(placeholderWidthBeforeZoom);

        // ビューポートに入っていた(=ロード済みだった)ページは新しい倍率で再描画される。
        vm.PageSlots[0].Image.Value.ShouldNotBeNull();
        vm.PageSlots[1].Image.Value.ShouldNotBeNull();

        // 一度もビューポートに入っていないページは引き続き未描画のまま。
        vm.PageSlots[2].Image.Value.ShouldBeNull();
    }

    [Fact]
    public async Task NextPageCommand_And_PreviousPageCommand_MoveCurrentPageIndexWithinBounds()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.NextPageCommand.CanExecute().ShouldBeTrue();
        vm.NextPageCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(1);

        vm.NextPageCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(2);

        // 最終ページでは次へ進めない。
        vm.NextPageCommand.CanExecute().ShouldBeFalse();

        vm.PreviousPageCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(1);
    }

    [Fact]
    public async Task PreviousPageCommand_CannotExecute_OnTheFirstPage()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.PreviousPageCommand.CanExecute().ShouldBeFalse();
    }

    [Fact]
    public async Task JumpToPageCommand_MovesToTheSpecifiedPage_ButIgnoresOutOfRangeValues()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 5);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.JumpToPageCommand.Execute(3);
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(3);

        vm.JumpToPageCommand.Execute(99);
        await WaitUntilIdleAsync(vm);
        vm.CurrentPageIndex.Value.ShouldBe(3);
    }

    [Fact]
    public async Task ZoomInCommand_And_ZoomOutCommand_ChangeZoomScaleWithinBounds()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 1);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        var initial = vm.ZoomScale.Value;
        vm.ZoomInCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.ZoomScale.Value.ShouldBeGreaterThan(initial);

        vm.ZoomOutCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.ZoomOutCommand.Execute();
        await WaitUntilIdleAsync(vm);
        vm.ZoomScale.Value.ShouldBeLessThan(initial);
    }

    [Fact]
    public async Task LoadAsync_CalledASecondTime_ResetsPageIndexAndZoom()
    {
        var (vm, metadata, _, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\first.pdf", pageCount: 5);
        metadata.RegisterSuccess(@"C:\out\second.pdf", pageCount: 2);

        await vm.LoadAsync(@"C:\out\first.pdf");
        await WaitUntilIdleAsync(vm);
        vm.JumpToPageCommand.Execute(4);
        await WaitUntilIdleAsync(vm);
        vm.ZoomInCommand.Execute();
        await WaitUntilIdleAsync(vm);

        await vm.LoadAsync(@"C:\out\second.pdf");
        await WaitUntilIdleAsync(vm);

        vm.PageCount.Value.ShouldBe(2);
        vm.CurrentPageIndex.Value.ShouldBe(0);
        vm.ZoomScale.Value.ShouldBe(1.0f);
    }

    // "AB"(1行目, Bottom=700)・"CD"(2行目, Bottom=680)の4文字を配置したテスト用フィクスチャ。
    private static readonly PdfTextLetter[] TwoLineLetters =
    [
        new("A", new PdfRect(Left: 0, Bottom: 700, Right: 10, Top: 710)),
        new("B", new PdfRect(Left: 10, Bottom: 700, Right: 20, Top: 710)),
        new("C", new PdfRect(Left: 0, Bottom: 680, Right: 10, Top: 690)),
        new("D", new PdfRect(Left: 10, Bottom: 680, Right: 20, Top: 690)),
    ];

    private static async Task<LinkEditorViewModel> CreateLoadedSutWithTwoLineLettersAsync(FakeMetadataService? metadataOverride = null)
    {
        var metadata = metadataOverride ?? new FakeMetadataService();
        var textExtractor = new FakePdfTextExtractor { Letters = TwoLineLetters };
        var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), textExtractor, metadata, new FakePdfLinkAnnotationService(), NullLogger<LinkEditorViewModel>.Instance);

        if (metadataOverride is null)
        {
            metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        }

        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);
        return vm;
    }

    [Fact]
    public async Task TextSelection_WithinASingleLine_ProducesOneLineRect()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.BeginTextSelection(pdfX: 2, pdfY: 705); // 'A'
        vm.UpdateTextSelection(pdfX: 15, pdfY: 705); // 'B'
        vm.EndTextSelection();

        var pending = vm.PendingSelection.Value.ShouldNotBeNull();
        pending.SourcePageIndex.ShouldBe(0);
        pending.LineRects.Count.ShouldBe(1);
        pending.LineRects[0].Left.ShouldBe(0);
        pending.LineRects[0].Right.ShouldBe(20);
    }

    [Fact]
    public async Task TextSelection_AcrossTwoLines_ProducesOneLineRectPerLine()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.BeginTextSelection(pdfX: 2, pdfY: 705); // 'A' (1行目)
        vm.UpdateTextSelection(pdfX: 15, pdfY: 685); // 'D' (2行目)
        vm.EndTextSelection();

        var pending = vm.PendingSelection.Value.ShouldNotBeNull();
        pending.LineRects.Count.ShouldBe(2);
        pending.LineRects[0].Bottom.ShouldBe(700);
        pending.LineRects[1].Bottom.ShouldBe(680);
    }

    /// <summary>
    /// コードレビュー後のユーザー報告の回帰テスト: ドラッグ中は、単純な始点〜終点の対角線矩形ではなく、
    /// EndTextSelectionが最終確定するのと同じ行ごとの実際の文字の外接矩形がLiveSelectionLineRectsへ
    /// 反映され、確定(EndTextSelection)後はクリアされてPendingSelectionへ引き継がれることを検証する。
    /// </summary>
    [Fact]
    public async Task DraggingASelection_PopulatesLiveSelectionLineRects_AndClearsThemOnceConfirmed()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.LiveSelectionLineRects.Value.ShouldBeEmpty();

        vm.BeginTextSelection(pdfX: 2, pdfY: 705); // 'A'
        vm.LiveSelectionLineRects.Value.Count.ShouldBe(1, "1文字だけの選択でも実際の文字の矩形が即座に見えるべき");

        vm.UpdateTextSelection(pdfX: 15, pdfY: 685); // 'D'(2行目)まで拡張
        vm.LiveSelectionLineRects.Value.Count.ShouldBe(2, "1行目・2行目にまたがる選択なので2行分の矩形になるはず");

        vm.EndTextSelection();

        vm.LiveSelectionLineRects.Value.ShouldBeEmpty("確定後はPendingSelectionへ引き継がれ、ドラッグ中の表示はクリアされるはず");
        vm.PendingSelection.Value.ShouldNotBeNull();
    }

    /// <summary>
    /// ユーザー報告の回帰テスト: 「任意の位置」をジャンプ先として選ぶ操作は、テキスト選択の確定後、
    /// ソースとは別のページまでスクロール(=ページ送り、Lettersの再読み込みを伴う)してからクリックする
    /// 一連の流れが前提。ページを跨ぐたびにPendingSelection/IsPickingArbitraryTargetがリセットされて
    /// しまうと、任意の位置の指定自体が実質不可能になってしまう不具合があった。
    /// </summary>
    [Fact]
    public async Task PickingAnArbitraryTarget_SurvivesNavigatingToADifferentPage()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.BeginTextSelection(pdfX: 2, pdfY: 705);
        vm.UpdateTextSelection(pdfX: 15, pdfY: 705);
        vm.EndTextSelection();
        vm.PendingSelection.Value.ShouldNotBeNull();

        vm.BeginPickArbitraryTargetCommand.Execute();
        vm.IsPickingArbitraryTarget.Value.ShouldBeTrue();

        // ジャンプ先ページまでページ送りする(Lettersが再読み込みされ、以前はここでリセットされていた)。
        vm.NextPageCommand.Execute();
        await WaitUntilIdleAsync(vm);

        vm.PendingSelection.Value.ShouldNotBeNull("ページ送りでPendingSelectionがリセットされてはならない");
        vm.IsPickingArbitraryTarget.Value.ShouldBeTrue("ページ送りでIsPickingArbitraryTargetがリセットされてはならない");

        var targetPage = vm.CurrentPageIndex.Value;
        vm.PickArbitraryTargetAndCreateLink(targetPage, pdfX: 5, pdfY: 700);

        vm.Links.ShouldNotBeEmpty();
        vm.Links.ShouldAllBe(l => l.TargetPageIndex == targetPage);
        vm.PendingSelection.Value.ShouldBeNull();
        vm.IsPickingArbitraryTarget.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateLinkToBookmark_AddsOneLinkPerLineRect_SharingTheSameGroupId_AndCopiesTheBookmarksDestination()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();

        var bookmark = new BookmarkNode
        {
            SourceFileEntryId = Guid.Empty,
            OriginalPageIndex = 2,
            DestinationType = BookmarkDestinationType.XYZ,
            Left = 10,
            Top = 20,
            Zoom = 1.5,
        };

        vm.CreateLinkToBookmarkCommand.Execute(bookmark);

        vm.Links.Count.ShouldBe(2);
        vm.Links[0].GroupId.ShouldBe(vm.Links[1].GroupId);
        vm.Links.ShouldAllBe(l => l.TargetPageIndex == 2);
        vm.Links.ShouldAllBe(l => l.DestinationType == BookmarkDestinationType.XYZ);
        vm.Links.ShouldAllBe(l => l.Left == 10 && l.Top == 20 && l.Zoom == 1.5);
        vm.PendingSelection.Value.ShouldBeNull();
    }

    [Fact]
    public async Task PickArbitraryTargetAndCreateLink_WithoutFirstCallingBeginPickArbitraryTarget_DoesNothing()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 705);
        vm.EndTextSelection();

        // BeginPickArbitraryTargetCommandを実行していないため、IsPickingArbitraryTargetはfalseのまま。
        vm.PickArbitraryTargetAndCreateLink(targetPageIndex: 1, pdfX: 50, pdfY: 60);

        vm.Links.ShouldBeEmpty();
        vm.PendingSelection.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task BeginPickArbitraryTarget_ThenPickArbitraryTargetAndCreateLink_AddsAnXyzLink()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 705);
        vm.EndTextSelection();

        vm.BeginPickArbitraryTargetCommand.Execute();
        vm.IsPickingArbitraryTarget.Value.ShouldBeTrue();

        vm.PickArbitraryTargetAndCreateLink(targetPageIndex: 1, pdfX: 50, pdfY: 60);

        var link = vm.Links.ShouldHaveSingleItem();
        link.TargetPageIndex.ShouldBe(1);
        link.DestinationType.ShouldBe(BookmarkDestinationType.XYZ);
        link.Left.ShouldBe(50);
        link.Top.ShouldBe(60);
        link.Zoom.ShouldBeNull();
        vm.IsPickingArbitraryTarget.Value.ShouldBeFalse();
        vm.PendingSelection.Value.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteLinkGroup_RemovesAllLinksSharingThatGroupId_ButNotOthers()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });
        var firstGroupId = vm.Links[0].GroupId;

        vm.BeginTextSelection(2, 705);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 2 });

        vm.Links.Count.ShouldBe(3);

        vm.DeleteLinkGroupCommand.Execute(firstGroupId);

        vm.Links.Count.ShouldBe(1);
        vm.Links.ShouldAllBe(l => l.GroupId != firstGroupId);
    }

    [Fact]
    public async Task CancelPendingSelection_ClearsPendingSelectionAndArbitraryTargetMode()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.EndTextSelection();
        vm.BeginPickArbitraryTargetCommand.Execute();

        vm.CancelPendingSelectionCommand.Execute();

        vm.PendingSelection.Value.ShouldBeNull();
        vm.IsPickingArbitraryTarget.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task LinkGroups_ReflectsOneEntryPerGroupId_RegardlessOfHowManyLineRectsItHas()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        // 1件目: 2行にまたがる選択(2件のLinkAnnotationNode、同一GroupId)。
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });

        // 2件目: 1行のみの選択。
        vm.BeginTextSelection(2, 705);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 2 });

        vm.Links.Count.ShouldBe(3);
        vm.LinkGroups.Value.Count.ShouldBe(2);
        vm.LinkGroups.Value.ShouldContain(g => g.RectCount == 2 && g.TargetPageIndex == 1);
        vm.LinkGroups.Value.ShouldContain(g => g.RectCount == 1 && g.TargetPageIndex == 2);
    }

    [Fact]
    public async Task BeginEditLinkGroup_RemovesTheOldLinksAndRestoresThemAsAPendingSelection()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });
        var groupId = vm.Links[0].GroupId;

        vm.EditLinkGroupCommand.Execute(groupId);

        vm.Links.ShouldBeEmpty();
        var pending = vm.PendingSelection.Value.ShouldNotBeNull();
        pending.LineRects.Count.ShouldBe(2);

        // 新しいジャンプ先を選び直すと、(新しいGroupIdで)リンクが復元される。
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 2 });
        vm.Links.Count.ShouldBe(2);
        vm.Links.ShouldAllBe(l => l.TargetPageIndex == 2);
    }

    [Fact]
    public async Task BeginEditLinkGroup_WithAnUnknownGroupId_DoesNothing()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();

        vm.EditLinkGroupCommand.Execute(Guid.NewGuid());

        vm.Links.ShouldBeEmpty();
        vm.PendingSelection.Value.ShouldBeNull();
    }

    [Fact]
    public async Task FinishAsync_RestoresThePristineBackupBeforeApplyingLinks_SoTheServiceSeesAFreshCopyEachTime()
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LinkEditorFinishAsyncTests_" + Guid.NewGuid());
        Directory.CreateDirectory(workDirectory);
        try
        {
            var filePath = Path.Combine(workDirectory, "merged.pdf");
            await File.WriteAllTextAsync(filePath, "original content");

            var metadata = new FakeMetadataService();
            metadata.RegisterSuccess(filePath, pageCount: 3);
            var linkAnnotationService = new FakePdfLinkAnnotationService();
            var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);

            await vm.LoadAsync(filePath);
            await WaitUntilIdleAsync(vm);

            var link = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 0, 10, 10),
                TargetPageIndex = 1,
            };
            vm.Links.Add(link);

            // ロード後にファイルの内容が変わっても(このアプリでは通常起きないが)、
            // FinishAsyncはロード直後のバックアップを基準に動作を続ける。
            await File.WriteAllTextAsync(filePath, "content changed after load, e.g. by another process");

            await vm.FinishAsync();

            linkAnnotationService.CallCount.ShouldBe(1);
            linkAnnotationService.LastFilePath.ShouldBe(filePath);
            linkAnnotationService.LastLinks.ShouldNotBeNull();
            linkAnnotationService.LastLinks.ShouldContain(link);
            (await File.ReadAllTextAsync(filePath)).ShouldBe("original content");

            // 2回目もバックアップから復元されるため、1回目の呼び出しの影響を引きずらない
            // (実際にはApplyLinksAsyncは注釈を書き込むが、フェイクなのでファイル内容自体は変わらない —
            // ここではFinishAsyncが毎回バックアップへ復元してから呼び出す、という手順自体を検証する)。
            await vm.FinishAsync();
            linkAnnotationService.CallCount.ShouldBe(2);
            (await File.ReadAllTextAsync(filePath)).ShouldBe("original content");
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FinishAsync_WithNoPristineBackup_DoesNothingAndLeavesLinksIntact()
    {
        // CreateLoadedSutWithTwoLineLettersAsyncはフィクションのパス(C:\out\merged.pdf)を使うため、
        // 実ファイルが存在せずバックアップが作られない = FinishAsyncは何もしない(何も投げず、
        // 既存のLinksもそのまま)。実ファイルを使った完全な経路は上のテストで検証済み。
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });

        await vm.FinishAsync();

        vm.Links.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LoadAsync_PopulatesLinksWithExistingLinksFromTheFile_MarkedAsPreExisting()
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LinkEditorExistingLinksTests_" + Guid.NewGuid());
        Directory.CreateDirectory(workDirectory);
        try
        {
            var filePath = Path.Combine(workDirectory, "merged.pdf");
            await File.WriteAllTextAsync(filePath, "original content");

            var metadata = new FakeMetadataService();
            metadata.RegisterSuccess(filePath, pageCount: 3);
            var linkAnnotationService = new FakePdfLinkAnnotationService();
            var existingLink = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 0, 10, 10),
                TargetPageIndex = 2,
            };
            linkAnnotationService.ExistingLinksByFilePath[filePath] = [existingLink];
            var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);

            await vm.LoadAsync(filePath);
            await WaitUntilIdleAsync(vm);

            vm.Links.ShouldContain(existingLink);
            vm.LinkGroups.Value.ShouldHaveSingleItem().IsPreExisting.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    /// <summary>
    /// ユーザー要望の回帰テスト: 既存(IsPreExisting)のリンクグループは、現在プレビューが
    /// アクティブになっているページのものだけを一覧表示する(大量の既存リンクが一覧を占有するのを
    /// 避けるため)。ページを移動すると、そのページの既存リンクが一覧に現れる。
    /// </summary>
    [Fact]
    public async Task LinkGroups_HidesPreExistingGroupsOnOtherPages_ButShowsThemOnceThatPageIsActive()
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LinkEditorExistingLinksTests_" + Guid.NewGuid());
        Directory.CreateDirectory(workDirectory);
        try
        {
            var filePath = Path.Combine(workDirectory, "merged.pdf");
            await File.WriteAllTextAsync(filePath, "original content");

            var metadata = new FakeMetadataService();
            metadata.RegisterSuccess(filePath, pageCount: 3);
            var linkAnnotationService = new FakePdfLinkAnnotationService();
            var existingLinkOnPage2 = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 2,
                SourceRect = new PdfRect(0, 0, 10, 10),
                TargetPageIndex = 0,
            };
            linkAnnotationService.ExistingLinksByFilePath[filePath] = [existingLinkOnPage2];
            var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);

            await vm.LoadAsync(filePath);
            await WaitUntilIdleAsync(vm);

            // 読込直後はページ0が現在ページ。既存リンクはページ2にあるため、一覧には表示されない。
            vm.CurrentPageIndex.Value.ShouldBe(0);
            vm.Links.ShouldContain(existingLinkOnPage2, "Linksコレクション自体には保持し続ける(オーバーレイ描画等で必要)");
            vm.LinkGroups.Value.ShouldBeEmpty();

            vm.JumpToPageCommand.Execute(2);
            await WaitUntilIdleAsync(vm);

            vm.LinkGroups.Value.ShouldHaveSingleItem().IsPreExisting.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    /// <summary>このアプリ自身で新規作成したグループは、既存グループと異なりページを問わず常に一覧表示する。</summary>
    [Fact]
    public async Task LinkGroups_AlwaysShowsNewlyCreatedGroups_RegardlessOfCurrentPage()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(pdfX: 2, pdfY: 705);
        vm.UpdateTextSelection(pdfX: 15, pdfY: 705);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });

        vm.LinkGroups.Value.ShouldHaveSingleItem().IsPreExisting.ShouldBeFalse();

        vm.JumpToPageCommand.Execute(2);
        await WaitUntilIdleAsync(vm);

        vm.LinkGroups.Value.ShouldHaveSingleItem("新規作成したグループはページを移動しても一覧から消えないはず");
    }

    [Fact]
    public async Task DeleteLinkGroup_AndBeginEditLinkGroup_OnAPreExistingGroup_DoNothing()
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LinkEditorExistingLinksTests_" + Guid.NewGuid());
        Directory.CreateDirectory(workDirectory);
        try
        {
            var filePath = Path.Combine(workDirectory, "merged.pdf");
            await File.WriteAllTextAsync(filePath, "original content");

            var metadata = new FakeMetadataService();
            metadata.RegisterSuccess(filePath, pageCount: 3);
            var linkAnnotationService = new FakePdfLinkAnnotationService();
            var existingLink = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 0, 10, 10),
                TargetPageIndex = 2,
            };
            linkAnnotationService.ExistingLinksByFilePath[filePath] = [existingLink];
            var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);

            await vm.LoadAsync(filePath);
            await WaitUntilIdleAsync(vm);

            vm.DeleteLinkGroup(existingLink.GroupId);
            vm.Links.ShouldContain(existingLink);

            vm.BeginEditLinkGroup(existingLink.GroupId);
            vm.Links.ShouldContain(existingLink);
            vm.PendingSelection.Value.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FinishAsync_ExcludesPreExistingLinks_ButIncludesNewlyCreatedOnes()
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "LinkEditorExistingLinksTests_" + Guid.NewGuid());
        Directory.CreateDirectory(workDirectory);
        try
        {
            var filePath = Path.Combine(workDirectory, "merged.pdf");
            await File.WriteAllTextAsync(filePath, "original content");

            var metadata = new FakeMetadataService();
            metadata.RegisterSuccess(filePath, pageCount: 3);
            var linkAnnotationService = new FakePdfLinkAnnotationService();
            var existingLink = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 0, 10, 10),
                TargetPageIndex = 2,
            };
            linkAnnotationService.ExistingLinksByFilePath[filePath] = [existingLink];
            var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), new FakePdfTextExtractor(), metadata, linkAnnotationService, NullLogger<LinkEditorViewModel>.Instance);

            await vm.LoadAsync(filePath);
            await WaitUntilIdleAsync(vm);

            var newLink = new LinkAnnotationNode
            {
                GroupId = Guid.NewGuid(),
                SourcePageIndex = 1,
                SourceRect = new PdfRect(0, 0, 20, 20),
                TargetPageIndex = 0,
            };
            vm.Links.Add(newLink);

            await vm.FinishAsync();

            linkAnnotationService.LastLinks.ShouldNotBeNull();
            linkAnnotationService.LastLinks.ShouldContain(newLink);
            linkAnnotationService.LastLinks.ShouldNotContain(existingLink);
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    /// <summary>
    /// ページ送り系コマンド(Previous/Next/Zoom/JumpToPage)は全てIsBusyをCanExecuteへ含めている。
    /// リンク操作系コマンドも一貫してこれに合わせるよう修正した回帰テスト
    /// (コードレビューで発見した一貫性の欠如を修正)。
    /// </summary>
    [Fact]
    public async Task LinkOperationCommands_CanExecute_AreAllFalseWhileBusy()
    {
        var vm = await CreateLoadedSutWithTwoLineLettersAsync();
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();
        vm.CreateLinkToBookmarkCommand.Execute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 1 });
        var groupId = vm.LinkGroups.Value[0].GroupId;

        // 選択待ち状態を作り直してから、CanExecuteの前提(PendingSelectionあり)を満たした状態でIsBusyだけを操作する。
        vm.BeginTextSelection(2, 705);
        vm.UpdateTextSelection(15, 685);
        vm.EndTextSelection();
        vm.PendingSelection.Value.ShouldNotBeNull();

        // ReactiveCommandのCanExecuteは、元になるIObservable&lt;bool&gt;(ここではIsBusy由来)の
        // 変化から1回スケジューリングを挟んで反映されるため、値を変更した直後はawait Task.Yield()で
        // 一度制御を返してから確認する必要がある(BookmarkTreeViewModelTests.
        // UndoCommand_CanExecute_IsFalseWhileIsBusy_EvenWithUndoHistoryと同じ、既存の確立された
        // パターン。これを省略すると低頻度のflaky failureになることを実際に確認した)。
        vm.IsBusy.Value = true;
        await Task.Yield();

        vm.CancelPendingSelectionCommand.CanExecute().ShouldBeFalse();
        vm.BeginPickArbitraryTargetCommand.CanExecute().ShouldBeFalse();
        ((ICommand)vm.CreateLinkToBookmarkCommand).CanExecute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 0 }).ShouldBeFalse();
        ((ICommand)vm.DeleteLinkGroupCommand).CanExecute(groupId).ShouldBeFalse();
        ((ICommand)vm.EditLinkGroupCommand).CanExecute(groupId).ShouldBeFalse();

        vm.IsBusy.Value = false;
        await Task.Yield();

        vm.CancelPendingSelectionCommand.CanExecute().ShouldBeTrue();
        vm.BeginPickArbitraryTargetCommand.CanExecute().ShouldBeTrue();
        ((ICommand)vm.CreateLinkToBookmarkCommand).CanExecute(new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 0 }).ShouldBeTrue();
        ((ICommand)vm.DeleteLinkGroupCommand).CanExecute(groupId).ShouldBeTrue();
        ((ICommand)vm.EditLinkGroupCommand).CanExecute(groupId).ShouldBeTrue();
    }
}
