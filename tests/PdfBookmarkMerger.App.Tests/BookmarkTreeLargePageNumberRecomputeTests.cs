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

    private static async Task<BookmarkTreeViewModel> CreateSutWithFlatTreeAsync(int nodeCount)
    {
        // Strings.Cultureはプロセス全体で共有される静的状態。他のテストが英語に切り替えたままに
        // なっていないよう、既定(日本語)へ明示的に戻してから使う。
        Strings.Culture = null;

        var dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);

        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new BookmarkNode { SourceFileEntryId = FileId, Title = $"Page{i}", OriginalPageIndex = i, MergedPageIndex = i })
            .ToList();

        // 大量ノードの場合、LoadAsync自体がRecomputeChunkSizeごとにawait Task.Yield()するため、
        // ここできちんとawaitする(BookmarkNodeViewModelの構築自体がチャンク処理される回帰テストの対象)。
        await vm.LoadAsync(nodes, new Dictionary<Guid, string> { [FileId] = "large.pdf" }, [FileId]);
        return vm;
    }

    /// <summary>
    /// Load/AddRoot/Undo等がバックグラウンドで起動した再計算(TriggerRecompute)の完了を待つ。
    /// IsBusyがfalseを1回観測しただけで返すと、テストスイート全体を並列実行する負荷が高い場合に、
    /// 別のチャンク処理が次のawait Task.Yield()から再開する前のごく短い間隙をfalseとして
    /// 誤観測してしまうことがまれにあった(スレッドプールの混雑時にのみ再現するflaky failureとして
    /// 実際に確認した)。falseを2回連続で観測できるまで待つことで、この種の誤検知を避ける。
    /// </summary>
    private static async Task WaitUntilIdleAsync(BookmarkTreeViewModel vm)
    {
        while (true)
        {
            while (vm.IsBusy.Value)
            {
                await Task.Delay(1);
            }

            await Task.Delay(1);
            if (!vm.IsBusy.Value)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task RecomputeAllPageNumberDisplaysAsync_OnASmallTree_NeverTogglesIsBusy()
    {
        var vm = await CreateSutWithFlatTreeAsync(5);
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
        var vm = await CreateSutWithFlatTreeAsync(nodeCount);
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
        var vm = await CreateSutWithFlatTreeAsync(5000);
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
        var vm = await CreateSutWithFlatTreeAsync(BookmarkTreeViewModel.RecomputeChunkSize * 2);
        await WaitUntilIdleAsync(vm);

        vm.RootNodes[0].PreOffsetPageNumber.Value = 999;
        await WaitUntilIdleAsync(vm);
        vm.HasPageNumberEdits.Value.ShouldBeTrue();

        vm.UndoCommand.Execute();
        await WaitUntilIdleAsync(vm);

        vm.RootNodes[0].PreOffsetPageNumber.Value.ShouldBe(1);
        vm.HasPageNumberEdits.Value.ShouldBeFalse();
    }

    /// <summary>
    /// コードレビューで発見・修正した不具合の回帰テスト: BookmarkTreeViewModel.RebuildTreeAsync
    /// (LoadAsync・UndoAsyncの下位処理)は、BookmarkNodeViewModelの構築自体(1ノードあたり8個の
    /// ReactivePropertySlim+Skip(1).Subscribe・4個のCombineLatest等、決して軽くないRx購読の組み立てを
    /// 伴う)をRecomputeAllPageNumberDisplaysAsyncと同じ枠組みでチャンク処理する。修正前は、この構築
    /// ループがBookmarkNodeViewModelのコンストラクタの再帰呼び出しに閉じ込められておりawaitする機会が
    /// 一切無く、2000件規模のツリーで実測1分規模のUIスレッド専有(処理中オーバーレイの描画すら
    /// 行われない)を引き起こしていた。
    /// </summary>
    [Fact]
    public async Task LoadAsync_OnALargeTree_TogglesIsBusyAndReportsIntermediateProgress_WhileConstructingViewModels()
    {
        Strings.Culture = null;
        var dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);

        var nodeCount = (BookmarkTreeViewModel.RecomputeChunkSize * 3) + 1;
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new BookmarkNode { SourceFileEntryId = FileId, Title = $"Page{i}", OriginalPageIndex = i, MergedPageIndex = i })
            .ToList();

        var busyStates = new List<bool>();
        using var busySub = vm.IsBusy.Subscribe(busyStates.Add);
        var progressSnapshots = new List<BusyProgressInfo?>();
        using var progressSub = vm.BusyProgress.Subscribe(progressSnapshots.Add);

        await vm.LoadAsync(nodes, new Dictionary<Guid, string> { [FileId] = "large.pdf" }, [FileId]);
        await WaitUntilIdleAsync(vm);

        busyStates.ShouldContain(true, "大量ノードのViewModel構築中はIsBusyがtrueになるはず");
        progressSnapshots
            .Any(p => p != null && p.CompletedCount > 0 && p.CompletedCount < p.TotalCount)
            .ShouldBeTrue("構築中の途中経過が1回も報告されていない(チャンク分割・await Task.Yield()が効いていない)");
        vm.RootNodes.Count.ShouldBe(nodeCount);
    }

    /// <summary>Undo(元に戻す)も、RebuildTreeAsync経由でLoadAsyncと同じ構築チャンク処理の恩恵を受けることを確認する。</summary>
    [Fact]
    public async Task Undo_OnALargeTree_TogglesIsBusyWhileRebuildingViewModels()
    {
        var vm = await CreateSutWithFlatTreeAsync(BookmarkTreeViewModel.RecomputeChunkSize * 3);
        await WaitUntilIdleAsync(vm);

        vm.AddRoot();
        await WaitUntilIdleAsync(vm);

        var busyStates = new List<bool>();
        using var sub = vm.IsBusy.Subscribe(busyStates.Add);

        // UndoCommand.Execute()(fire-and-forgetのTriggerUndo経由)ではなく、内部の
        // UndoAsyncを直接awaitする。WaitUntilIdleAsyncによるポーリングは、テストスイート全体を
        // 並列実行する負荷が高い場面でごくまれに誤検知することがあり(WaitUntilIdleAsync自体の
        // コメント参照)、この検証の主旨(Undo中に本当にIsBusyがtrue→falseと遷移するか)にとっては
        // 直接awaitする方がポーリングに頼らず確実。
        await vm.UndoAsync();

        busyStates.ShouldContain(true, "大量ノードのUndoによる再構築中はIsBusyがtrueになるはず");
        busyStates[^1].ShouldBeFalse();
    }
}
