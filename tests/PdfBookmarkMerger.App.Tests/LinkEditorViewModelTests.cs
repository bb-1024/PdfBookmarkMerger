using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

public sealed class LinkEditorViewModelTests
{
    private static (LinkEditorViewModel Vm, FakeMetadataService Metadata) CreateSut()
    {
        var metadata = new FakeMetadataService();
        var vm = new LinkEditorViewModel(new FakePdfPageRenderer(), metadata, NullLogger<LinkEditorViewModel>.Instance);
        return (vm, metadata);
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
        var (vm, metadata) = CreateSut();
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
        var (vm, metadata) = CreateSut();
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
        var (vm, metadata) = CreateSut();
        metadata.RegisterSuccess(@"C:\out\merged.pdf", pageCount: 3);
        await vm.LoadAsync(@"C:\out\merged.pdf");
        await WaitUntilIdleAsync(vm);

        vm.PreviousPageCommand.CanExecute().ShouldBeFalse();
    }

    [Fact]
    public async Task JumpToPageCommand_MovesToTheSpecifiedPage_ButIgnoresOutOfRangeValues()
    {
        var (vm, metadata) = CreateSut();
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
        var (vm, metadata) = CreateSut();
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
        var (vm, metadata) = CreateSut();
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
}
