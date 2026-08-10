using System.Diagnostics;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// ユーザー報告(しおりが大量にある状態で編集・元に戻す操作を行うと、進捗表示も無いままUIが長時間
/// フリーズする)の修正確認。BookmarkTreeViewModel.RecomputeAllPageNumberDisplaysAsyncは、
/// ノード数がRecomputeChunkSizeを超えるツリーに対してのみ、await Task.Yield()を挟みながら
/// チャンク単位で処理し、その間IsBusy/BusyProgressで進捗を報告する。これにより、構造編集メソッド群
/// (AddRoot・Undo等)は重い再計算の完了を待たずに即座に呼び出し元へ制御を返せる。
/// ノード数がしきい値以下の小規模なツリーでは、これまでどおり一度もawaitせず同期的に完了することも
/// あわせて検証する(既存のテスト・コードビハインドが無改修で動作し続けることの裏付け)。
/// </summary>
public sealed class BookmarkTreeLargePageNumberRecomputeTests
{
    private static readonly Guid FileId = Guid.NewGuid();

    private static BookmarkTreeViewModel CreateSutWithFlatTree(int nodeCount, out FakeDialogService dialog)
    {
        // Strings.Cultureはプロセス全体で共有される静的状態。他のテストが英語に切り替えたままに
        // なっていないよう、既定(日本語)へ明示的に戻してから使う。
        Strings.Culture = null;

        dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);

        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new BookmarkNode { SourceFileEntryId = FileId, Title = $"Page{i}", OriginalPageIndex = i, MergedPageIndex = i })
            .ToList();

        vm.Load(nodes, new Dictionary<Guid, string> { [FileId] = "large.pdf" }, [FileId]);
        return vm;
    }

    /// <summary>Load/AddRoot/Undo等がバックグラウンドで起動した再計算(TriggerRecompute)の完了を待つ。</summary>
    private static async Task WaitUntilIdleAsync(BookmarkTreeViewModel vm)
    {
        while (vm.IsBusy.Value)
        {
            await Task.Delay(1);
        }
    }

    [Fact]
    public async Task RecomputeAllPageNumberDisplaysAsync_OnASmallTree_NeverTogglesIsBusy()
    {
        var vm = CreateSutWithFlatTree(5, out _);
        await WaitUntilIdleAsync(vm);

        var busyStates = new List<bool>();
        using var sub = vm.IsBusy.Subscribe(busyStates.Add);

        await vm.RecomputeAllPageNumberDisplaysAsync();

        busyStates.ShouldAllBe(b => b == false, "小規模なツリーでは処理中オーバーレイを出す必要が無く、ちらつきを避けるべき");
    }

    [Fact]
    public async Task RecomputeAllPageNumberDisplaysAsync_OnALargeTree_TogglesIsBusyAndReportsIntermediateProgress()
    {
        var nodeCount = (BookmarkTreeViewModel.RecomputeChunkSize * 3) + 1;
        var vm = CreateSutWithFlatTree(nodeCount, out _);
        await WaitUntilIdleAsync(vm);

        var busyStates = new List<bool>();
        using var busySub = vm.IsBusy.Subscribe(busyStates.Add);
        var progressSnapshots = new List<BusyProgressInfo?>();
        using var progressSub = vm.BusyProgress.Subscribe(progressSnapshots.Add);

        await vm.RecomputeAllPageNumberDisplaysAsync();

        busyStates.ShouldContain(true);
        busyStates[^1].ShouldBeFalse();
        progressSnapshots
            .Any(p => p != null && p.CompletedCount > 0 && p.CompletedCount < p.TotalCount)
            .ShouldBeTrue("途中経過の進捗が1回も報告されていない(チャンク分割・await Task.Yield()が効いていない)");
        progressSnapshots[^1].ShouldBeNull();
    }

    [Fact]
    public async Task AddRoot_OnAVeryLargeTree_ReturnsWithoutWaitingForTheFullRecomputeToFinish()
    {
        var vm = CreateSutWithFlatTree(5000, out _);
        await WaitUntilIdleAsync(vm);

        var stopwatch = Stopwatch.StartNew();
        vm.AddRoot();
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2),
            "AddRootは重い全件再計算をバックグラウンドへ委譲するはずで、5000ノード分の再計算完了を" +
            "待ってから戻る(=UIスレッドをブロックする)実装に退行していないか確認する");

        await WaitUntilIdleAsync(vm);
    }

    [Fact]
    public async Task Undo_OnALargeTree_EventuallyRecomputesAllDisplaysCorrectly()
    {
        var vm = CreateSutWithFlatTree(BookmarkTreeViewModel.RecomputeChunkSize * 2, out _);
        await WaitUntilIdleAsync(vm);

        vm.RootNodes[0].PreOffsetPageNumber.Value = 999;
        await WaitUntilIdleAsync(vm);
        vm.HasPageNumberEdits.Value.ShouldBeTrue();

        vm.UndoCommand.Execute();
        await WaitUntilIdleAsync(vm);

        vm.RootNodes[0].PreOffsetPageNumber.Value.ShouldBe(1);
        vm.HasPageNumberEdits.Value.ShouldBeFalse();
    }
}
