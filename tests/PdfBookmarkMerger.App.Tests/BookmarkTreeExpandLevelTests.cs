using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// しおり編集画面のツリー開閉コントロール(「-」「+」ボタン・開閉レベルテキストボックス)を検証する。
/// レベルNを指定すると、レベルN以下のノードはIsExpanded=true(開いた状態)、レベルNを超えるノードは
/// IsExpanded=false(閉じた状態)になる(例: N=3ならレベル1〜3が開き、レベル4以降が閉じる)。
/// テキストボックスに数値以外・ツリーに含まれない数値が入力された場合、フォーカスを失った際
/// (NormalizeExpandLevelInput)に空欄へ正規化される。しおり側の構造編集によって現在の入力値が
/// ツリーに含まれなくなった場合も、各構造編集メソッドの内部から同じ正規化が行われる。
/// </summary>
public sealed class BookmarkTreeExpandLevelTests
{
    private static readonly Guid FileId = Guid.NewGuid();

    private static BookmarkTreeViewModel CreateSut(out FakeDialogService dialog)
    {
        // Strings.Cultureはプロセス全体で共有される静的状態。他のテストが英語に切り替えたままに
        // なっていないよう、既定(日本語)へ明示的に戻してから使う。
        Strings.Culture = null;

        dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);
        vm.Load([], new Dictionary<Guid, string> { [FileId] = "sample.pdf" }, [FileId]);
        return vm;
    }

    /// <summary>ルート→子→孫→曾孫の4階層(レベル1〜4)からなるツリーを構築する。</summary>
    private static (BookmarkTreeViewModel Tree, BookmarkNodeViewModel Level1, BookmarkNodeViewModel Level2, BookmarkNodeViewModel Level3, BookmarkNodeViewModel Level4)
        CreateSutWithFourLevels()
    {
        var vm = CreateSut(out _);
        var level1 = vm.AddRoot();
        var level2 = vm.AddChild(level1);
        var level3 = vm.AddChild(level2);
        var level4 = vm.AddChild(level3);
        return (vm, level1, level2, level3, level4);
    }

    private static BookmarkTreeViewModel CreateSutWithFlatTree(int nodeCount, out FakeDialogService dialog)
    {
        Strings.Culture = null;

        dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);

        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new BookmarkNode { SourceFileEntryId = FileId, Title = $"Page{i}", OriginalPageIndex = i, MergedPageIndex = i })
            .ToList();

        vm.Load(nodes, new Dictionary<Guid, string> { [FileId] = "large.pdf" }, [FileId]);
        return vm;
    }

    private static async Task WaitUntilIdleAsync(BookmarkTreeViewModel vm)
    {
        while (vm.IsBusy.Value)
        {
            await Task.Delay(1);
        }
    }

    [Fact]
    public void ExpandLevelInput_SetToValidLevel_OpensNodesAtOrBelowLevelAndClosesDeeperNodes()
    {
        var (vm, level1, level2, level3, level4) = CreateSutWithFourLevels();

        vm.ExpandLevelInput.Value = "3";

        level1.IsExpanded.Value.ShouldBeTrue();
        level2.IsExpanded.Value.ShouldBeTrue();
        level3.IsExpanded.Value.ShouldBeTrue();
        level4.IsExpanded.Value.ShouldBeFalse();
    }

    [Fact]
    public void ExpandLevelInput_SetToZero_ClosesEveryNodeIncludingRoot()
    {
        var (vm, level1, level2, level3, level4) = CreateSutWithFourLevels();

        vm.ExpandLevelInput.Value = "0";

        level1.IsExpanded.Value.ShouldBeFalse();
        level2.IsExpanded.Value.ShouldBeFalse();
        level3.IsExpanded.Value.ShouldBeFalse();
        level4.IsExpanded.Value.ShouldBeFalse();
    }

    [Fact]
    public void ExpandLevelInput_SetToTreeMaxLevel_OpensEveryNode()
    {
        var (vm, level1, level2, level3, level4) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "0";

        vm.ExpandLevelInput.Value = "4";

        level1.IsExpanded.Value.ShouldBeTrue();
        level2.IsExpanded.Value.ShouldBeTrue();
        level3.IsExpanded.Value.ShouldBeTrue();
        level4.IsExpanded.Value.ShouldBeTrue();
    }

    [Fact]
    public void CollapseAllCommand_ClosesAllNodesAndWritesZeroToTheTextBox()
    {
        var (vm, level1, _, _, level4) = CreateSutWithFourLevels();

        vm.CollapseAllCommand.Execute();

        vm.ExpandLevelInput.Value.ShouldBe("0");
        level1.IsExpanded.Value.ShouldBeFalse();
        level4.IsExpanded.Value.ShouldBeFalse();
    }

    [Fact]
    public void ExpandAllCommand_OpensAllNodesAndWritesTheTreeMaxLevelToTheTextBox()
    {
        var (vm, level1, _, _, level4) = CreateSutWithFourLevels();
        vm.CollapseAllCommand.Execute();

        vm.ExpandAllCommand.Execute();

        vm.ExpandLevelInput.Value.ShouldBe("4");
        level1.IsExpanded.Value.ShouldBeTrue();
        level4.IsExpanded.Value.ShouldBeTrue();
    }

    [Fact]
    public void NormalizeExpandLevelInput_WithNonNumericText_ClearsTheTextBox()
    {
        var (vm, _, _, _, _) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "abc";

        vm.NormalizeExpandLevelInput();

        vm.ExpandLevelInput.Value.ShouldBe(string.Empty);
    }

    [Fact]
    public void NormalizeExpandLevelInput_WithNumberAboveTheTreeMaxLevel_ClearsTheTextBox()
    {
        var (vm, _, _, _, _) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "5";

        vm.NormalizeExpandLevelInput();

        vm.ExpandLevelInput.Value.ShouldBe(string.Empty);
    }

    [Fact]
    public void NormalizeExpandLevelInput_WithValidNumber_KeepsTheTextBoxUnchanged()
    {
        var (vm, _, _, _, _) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "2";

        vm.NormalizeExpandLevelInput();

        vm.ExpandLevelInput.Value.ShouldBe("2");
    }

    [Fact]
    public void Remove_ReducingTheTreeMaxLevel_AutomaticallyClearsANowOutOfRangeExpandLevelInput()
    {
        var (vm, _, _, _, level4) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "4";

        vm.Remove(level4);

        vm.ExpandLevelInput.Value.ShouldBe(string.Empty,
            "レベル4のノードを削除して最大レベルが3になったため、入力値4はツリーに含まれなくなり、" +
            "Removeの内部から呼ばれるNormalizeExpandLevelInputで自動的に空へ戻るはず");
    }

    [Fact]
    public void AddChild_IncreasingTheTreeMaxLevel_DoesNotClearAStillValidExpandLevelInput()
    {
        var (vm, _, _, _, level4) = CreateSutWithFourLevels();
        vm.ExpandLevelInput.Value = "2";

        vm.AddChild(level4);

        vm.ExpandLevelInput.Value.ShouldBe("2", "最大レベルが5に増えても、既存の入力値2は引き続き有効なはず");
    }

    [Fact]
    public async Task CollapseAllCommand_And_ExpandAllCommand_CanExecute_AreFalseWhileIsBusy()
    {
        var (vm, _, _, _, _) = CreateSutWithFourLevels();
        vm.CollapseAllCommand.CanExecute().ShouldBeTrue();
        vm.ExpandAllCommand.CanExecute().ShouldBeTrue();

        vm.IsBusy.Value = true;
        await Task.Yield();
        vm.CollapseAllCommand.CanExecute().ShouldBeFalse();
        vm.ExpandAllCommand.CanExecute().ShouldBeFalse();

        vm.IsBusy.Value = false;
        await Task.Yield();
        vm.CollapseAllCommand.CanExecute().ShouldBeTrue();
        vm.ExpandAllCommand.CanExecute().ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyExpandLevelAsync_OnALargeTree_TogglesIsBusyAndAppliesTheLevelToAllNodes()
    {
        var nodeCount = (BookmarkTreeViewModel.RecomputeChunkSize * 2) + 1;
        var vm = CreateSutWithFlatTree(nodeCount, out _);
        await WaitUntilIdleAsync(vm);

        var busyStates = new List<bool>();
        using var sub = vm.IsBusy.Subscribe(busyStates.Add);

        await vm.ApplyExpandLevelAsync(0);

        busyStates.ShouldContain(true, "大量ノードのチャンク処理中はIsBusyがtrueになるはず");
        busyStates[^1].ShouldBeFalse();
        vm.RootNodes.ShouldAllBe(n => !n.IsExpanded.Value);
    }

    [Fact]
    public async Task ApplyExpandLevelAsync_OnASmallTree_NeverTogglesIsBusy()
    {
        var vm = CreateSutWithFlatTree(5, out _);
        await WaitUntilIdleAsync(vm);

        var busyStates = new List<bool>();
        using var sub = vm.IsBusy.Subscribe(busyStates.Add);

        await vm.ApplyExpandLevelAsync(0);

        busyStates.ShouldAllBe(b => b == false, "小規模なツリーでは処理中オーバーレイを出す必要が無く、ちらつきを避けるべき");
    }
}
