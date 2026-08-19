using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

public sealed class LinkEditorViewModelTests
{
    private static (LinkEditorViewModel Vm, FakeMetadataService Metadata, FakePdfTextExtractor TextExtractor) CreateSut()
    {
        var metadata = new FakeMetadataService();
        var textExtractor = new FakePdfTextExtractor();
        var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), textExtractor, metadata, NullLogger<LinkEditorViewModel>.Instance);
        return (vm, metadata, textExtractor);
    }

    private static async Task WaitUntilIdleAsync(LinkEditorViewModel vm)
    {
        for (var i = 0; i < 100 && vm.IsBusy.Value; i++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task LoadAsync_PopulatesPageCountAndBookmarks_AndRendersTheFirstPage()
    {
        var (vm, metadata, _) = CreateSut();
        var bookmark = new BookmarkNode { SourceFileEntryId = Guid.Empty, OriginalPageIndex = 2, Title = "Chapter 1" };
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 5, bookmarks: [bookmark]);

        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.FilePath.Value.ShouldBe(@"C:\out\merged.pdf");
        vm.PageCount.Value.ShouldBe(5);
        vm.CurrentPageIndex.Value.ShouldBe(0);
        vm.Bookmarks.Value.ShouldHaveSingleItem().Title.ShouldBe("Chapter 1");
        vm.PageImage.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task NextPageCommand_And_PreviousPageCommand_MoveCurrentPageIndexWithinBounds()
    {
        var (vm, metadata, _) = CreateSut();
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
        var (vm, metadata, _) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.PreviousPageCommand.CanExecute().ShouldBeFalse();
    }

    [Fact]
    public async Task JumpToPageCommand_MovesToTheSpecifiedPage_ButIgnoresOutOfRangeValues()
    {
        var (vm, metadata, _) = CreateSut();
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
        var (vm, metadata, _) = CreateSut();
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
        var (vm, metadata, _) = CreateSut();
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
        var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), textExtractor, metadata, NullLogger<LinkEditorViewModel>.Instance);

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
}
