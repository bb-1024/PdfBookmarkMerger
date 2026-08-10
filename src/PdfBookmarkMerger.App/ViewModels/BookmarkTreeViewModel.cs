using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.Undo;
using PdfBookmarkMerger.Core.Models;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// しおり編集ツリー(手順3)のViewModel。D&Dによる並べ替え・再親子付け、追加・削除、
/// タイトル等の編集結果をCore.Models.BookmarkNodeツリーへ同期する。
/// </summary>
public sealed class BookmarkTreeViewModel : ViewModelBase
{
    /// <summary>同一ノード・同一プロパティへの連続変更を1回の編集とみなす時間幅。
    /// テキスト入力中の1文字ごとにUndo履歴が積み上がるのを防ぐ。</summary>
    private static readonly TimeSpan SnapshotCoalesceWindow = TimeSpan.FromMilliseconds(800);

    private readonly IDialogService _dialogService;
    private readonly Dictionary<Guid, bool> _preOverrideExpandState = [];
    private readonly Dictionary<Guid, BookmarkDestinationType> _preOverrideDestinationType = [];
    private readonly UndoHistory<string> _undoHistory = new();
    private readonly Dictionary<string, DateTime> _lastSnapshotPushAt = [];

    /// <summary>
    /// trueの間、PushUndoSnapshotを呼んでも履歴を積まない。新規追加ノードへ「一律で...設定」の
    /// 現在値を即座に適用する処理(ApplyGlobalExpandOverrideToNewNode等)は、追加操作自体の
    /// Undoスナップショットに含まれるべきであり、独立した2件目の履歴として積むべきではないため。
    /// </summary>
    private bool _suppressUndoSnapshots;

    private List<BookmarkNode> _rootModel = [];
    private IReadOnlyDictionary<Guid, string> _fileNames = new Dictionary<Guid, string>();
    private IReadOnlyList<Guid> _orderedFileIds = [];

    /// <summary>
    /// trueの間、PreOffsetPageNumberの変更通知(OnPreOffsetPageNumberChanged)を無視する。
    /// RecomputeAllPageNumberDisplaysAsyncが再計算結果を各ノードへ書き戻す際、その書き戻し自体が
    /// ユーザー編集として二重に処理・再帰してしまうのを防ぐ。
    /// </summary>
    private bool _isRecomputingPageNumbers;

    /// <summary>
    /// RecomputeAllPageNumberDisplaysAsyncが1回のawait区間で処理するノード数。この件数ごとに
    /// await Task.Yield()でUIスレッドへ制御を返し、描画・入力処理の機会を与える。
    /// ノード総数がこの値以下のツリーでは一度もawaitが発生せず、これまで通り同期的に完了する
    /// (小規模なツリーでの不要なオーバーヘッド・ちらつきを避けるため)。
    /// </summary>
    internal const int RecomputeChunkSize = 200;

    public BookmarkTreeViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;

        ForceFitForAll = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        ForceFitForAll.Subscribe(ApplyForceFitOverride).AddTo(Disposables);
        GlobalExpandOverride = new ReactivePropertySlim<bool?>(null).AddTo(Disposables);
        GlobalExpandOverride.Subscribe(ApplyGlobalExpandOverride).AddTo(Disposables);
        TitleColumnBaseWidth = new ReactivePropertySlim<double>(DefaultTitleColumnBaseWidth).AddTo(Disposables);

        CanUndo = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        UndoCommand = new ReactiveCommand(CanUndo).AddTo(Disposables);
        UndoCommand.Subscribe(Undo).AddTo(Disposables);

        HasPageNumberEdits = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        HasPageNumberInconsistency = new ReactivePropertySlim<bool>(false).AddTo(Disposables);

        IsBusy = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        BusyProgress = new ReactivePropertySlim<BusyProgressInfo?>(null).AddTo(Disposables);
    }

    /// <summary>タイトル列の既定幅(px)。実際の幅はUI側でタイトル文字列の実測幅に応じて拡張される。</summary>
    public const double DefaultTitleColumnBaseWidth = 220;

    /// <summary>
    /// しおりツリーのタイトル列の基準幅(px、深さ0の場合の幅)。各行のタイトルの内容に追従して
    /// UI側(MainWindow.xaml.cs等)が実測・更新する。深さに応じた縮小は表示側のコンバータが行う。
    /// </summary>
    public ReactivePropertySlim<double> TitleColumnBaseWidth { get; }

    public ObservableCollection<BookmarkNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// オンの間、全ノードの表示方法・座標コントロールを不活性化し、結合時は全ノードをFitとして扱う。
    /// 個々のノードの設定値自体は変更しないため、オフに戻すと元の設定が復元される。
    /// </summary>
    public ReactivePropertySlim<bool> ForceFitForAll { get; }

    /// <summary>
    /// 「一律で展開表示を設定」の3状態(true=全展開/false=全収納/null=個別設定に従う)。
    /// true/falseの間、個々のIsOpenを一時的に上書きし元の値をキャッシュする。nullに戻すと復元する。
    /// </summary>
    public ReactivePropertySlim<bool?> GlobalExpandOverride { get; }

    /// <summary>元に戻せる履歴が存在するか。「元に戻す」ボタンのIsEnabledに直接バインドする。</summary>
    public ReactivePropertySlim<bool> CanUndo { get; }

    public ReactiveCommand UndoCommand { get; }

    /// <summary>
    /// ツリー内のいずれかのしおりの結合前ページ数が編集されているか(差分が実質0でない)。
    /// trueの間、結合後PDFの実際のページ位置と画面表示・書き出し内容が食い違うため
    /// 「結合してPDFを保存」を非活性化する(MainWindowViewModel.MergeCommandのCanExecuteに組み込む)。
    /// </summary>
    public ReactivePropertySlim<bool> HasPageNumberEdits { get; }

    /// <summary>
    /// 編集の結果、結合前・結合後いずれかのページ数が1未満(不整合)になっているノードが存在するか。
    /// trueの間は「結合してPDFを保存」「しおり設定ファイルを保存」の両方を非活性化する。
    /// </summary>
    public ReactivePropertySlim<bool> HasPageNumberInconsistency { get; }

    /// <summary>
    /// 結合前ページ数の再計算(RecomputeAllPageNumberDisplaysAsync)が大量のノードを対象に
    /// バックグラウンドで進行中か。しおりが大量にある状態での編集・追加・削除・元に戻す操作は
    /// この再計算を伴うため、trueの間はMainWindowViewModel側のIsBusyへ転送され、既存の
    /// 処理中オーバーレイで操作をブロックしつつ進捗を表示する(小規模なツリーでは一度もtrueにならない)。
    /// </summary>
    public ReactivePropertySlim<bool> IsBusy { get; }

    /// <summary>IsBusy中の詳細進捗(完了/総ノード数)。IsBusyがfalseの間はnull。</summary>
    public ReactivePropertySlim<BusyProgressInfo?> BusyProgress { get; }

    /// <summary>
    /// 新規ドキュメントの読み込み。前のドキュメントのUndo履歴は引き継がない(クリアする)。
    /// orderedFileIdsは結合順のファイルID一覧(結合後ページ数の連鎖計算に使う、しおりツリー上の
    /// 表示順ではなくファイル一覧の並び順)。
    /// </summary>
    public void Load(IReadOnlyList<BookmarkNode> rootBookmarks, IReadOnlyDictionary<Guid, string> fileNames, IReadOnlyList<Guid> orderedFileIds)
    {
        _fileNames = fileNames;
        _orderedFileIds = orderedFileIds;
        RebuildTree(rootBookmarks);
        _undoHistory.Clear();
        _lastSnapshotPushAt.Clear();
        CanUndo.Value = false;
    }

    /// <summary>RootNodes/_rootModelを指定内容で再構築する。Load(新規読込)とUndo(履歴復元)の両方から使う、
    /// Undo履歴自体には触れない下位処理。</summary>
    private void RebuildTree(IReadOnlyList<BookmarkNode> rootBookmarks)
    {
        _rootModel = rootBookmarks.ToList();
        _preOverrideExpandState.Clear();
        _preOverrideDestinationType.Clear();

        RootNodes.Clear();
        foreach (var node in _rootModel)
        {
            var name = _fileNames.GetValueOrDefault(node.SourceFileEntryId, "?");
            RootNodes.Add(new BookmarkNodeViewModel(node, name, null, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot, OnPreOffsetPageNumberChanged));
        }

        TriggerRecompute();
    }

    /// <summary>
    /// 直前の状態をUndo履歴へ積む(構造的な操作の直前に呼ぶ、コアレス無し=常に1件積む)。
    /// </summary>
    private void PushUndoSnapshot() => PushUndoSnapshotCore(null);

    /// <summary>
    /// 直前の状態をUndo履歴へ積む(BookmarkNodeViewModelのプロパティ変更用)。
    /// 同一coalesceKeyからの連続呼び出しがSnapshotCoalesceWindow以内であれば、1回の編集とみなし
    /// 履歴を積み増さない。
    /// </summary>
    private void PushUndoSnapshot(string coalesceKey) => PushUndoSnapshotCore(coalesceKey);

    private void PushUndoSnapshotCore(string? coalesceKey)
    {
        if (_suppressUndoSnapshots)
        {
            return;
        }

        if (coalesceKey is not null)
        {
            var now = DateTime.UtcNow;
            if (_lastSnapshotPushAt.TryGetValue(coalesceKey, out var last) && now - last < SnapshotCoalesceWindow)
            {
                _lastSnapshotPushAt[coalesceKey] = now;
                return;
            }

            _lastSnapshotPushAt[coalesceKey] = now;
        }

        var json = JsonSerializer.Serialize(_rootModel);
        _undoHistory.Push(json, Encoding.UTF8.GetByteCount(json));
        CanUndo.Value = true;
    }

    private void Undo()
    {
        if (!_undoHistory.TryPop(out var json))
        {
            return;
        }

        var restored = JsonSerializer.Deserialize<List<BookmarkNode>>(json) ?? [];
        RebuildTree(restored);
        CanUndo.Value = _undoHistory.CanUndo;
    }

    private void ApplyGlobalExpandOverride(bool? overrideValue)
    {
        void Walk(IEnumerable<BookmarkNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (overrideValue is { } forced)
                {
                    _preOverrideExpandState.TryAdd(node.Model.Id, node.IsOpen.Value);
                    node.IsOpen.Value = forced;
                }
                else if (_preOverrideExpandState.TryGetValue(node.Model.Id, out var original))
                {
                    node.IsOpen.Value = original;
                    _preOverrideExpandState.Remove(node.Model.Id);
                }

                Walk(node.Children);
            }
        }

        // 「一律で展開表示を設定」は表示上の一時的な上書き(元の値はキャッシュ済みで自動復元される)であり、
        // Undo対象の「編集内容」ではない。ノード数分の履歴が一括で積まれるのを防ぐため抑止する。
        _suppressUndoSnapshots = true;
        try
        {
            Walk(RootNodes);
        }
        finally
        {
            _suppressUndoSnapshots = false;
        }
    }

    /// <summary>新規追加ノードにも、現在有効な一律展開表示設定を即座に適用する。</summary>
    private void ApplyGlobalExpandOverrideToNewNode(BookmarkNodeViewModel vm)
    {
        if (GlobalExpandOverride.Value is { } forced)
        {
            _preOverrideExpandState.TryAdd(vm.Model.Id, vm.IsOpen.Value);
            vm.IsOpen.Value = forced;
        }
    }

    /// <summary>
    /// 「一律でFitに設定」の表示方法(DestinationType)への反映。オンの間は全ノードの表示方法を
    /// Fitへ一時的に上書きし元の値をキャッシュする。オフに戻すと元の値へ復元する。
    /// </summary>
    private void ApplyForceFitOverride(bool forced)
    {
        void Walk(IEnumerable<BookmarkNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (forced)
                {
                    _preOverrideDestinationType.TryAdd(node.Model.Id, node.DestinationType.Value);
                    node.DestinationType.Value = BookmarkDestinationType.Fit;
                }
                else if (_preOverrideDestinationType.TryGetValue(node.Model.Id, out var original))
                {
                    node.DestinationType.Value = original;
                    _preOverrideDestinationType.Remove(node.Model.Id);
                }

                Walk(node.Children);
            }
        }

        // 「一律でFitに設定」も表示上の一時的な上書きであり、Undo対象の「編集内容」ではない。
        // ノード数分の履歴が一括で積まれるのを防ぐため抑止する。
        _suppressUndoSnapshots = true;
        try
        {
            Walk(RootNodes);
        }
        finally
        {
            _suppressUndoSnapshots = false;
        }
    }

    /// <summary>新規追加ノードにも、現在有効な一律Fit設定を即座に適用する。</summary>
    private void ApplyForceFitOverrideToNewNode(BookmarkNodeViewModel vm)
    {
        if (ForceFitForAll.Value)
        {
            _preOverrideDestinationType.TryAdd(vm.Model.Id, vm.DestinationType.Value);
            vm.DestinationType.Value = BookmarkDestinationType.Fit;
        }
    }

    /// <summary>
    /// 新規追加ノードへ「一律で...設定」の現在値を適用する(2つの上記メソッドをまとめて呼ぶ)。
    /// この適用自体は追加操作の一部として扱うため、Undoスナップショットは積まない
    /// (追加操作そのものの直前スナップショットに1つの編集としてまとめる)。
    /// </summary>
    private void ApplyCurrentOverridesToNewNode(BookmarkNodeViewModel vm)
    {
        _suppressUndoSnapshots = true;
        try
        {
            ApplyGlobalExpandOverrideToNewNode(vm);
            ApplyForceFitOverrideToNewNode(vm);
        }
        finally
        {
            _suppressUndoSnapshots = false;
        }
    }

    /// <summary>子孫に含まれる最大の相対レベル(直下の子=1)を返す。子がなければ0。</summary>
    public int ComputeMaxDescendantLevel(BookmarkNodeViewModel node) =>
        node.Children.Count == 0 ? 0 : 1 + node.Children.Max(ComputeMaxDescendantLevel);

    /// <summary>
    /// 子要素のレベル上限設定ダイアログを表示し、選択された上限より深い下位要素をすべて削除する。
    /// ダイアログで表示・選択するレベルは、しおり編集ツリーの表示と対応するルートから数えた絶対レベル。
    /// 選択肢には要素自身のレベルも含める(自身のレベルを選択した場合、子要素がすべて削除される)。
    /// </summary>
    public async Task SetChildLevelCapAsync(BookmarkNodeViewModel node)
    {
        var maxRelativeLevel = ComputeMaxDescendantLevel(node);
        if (maxRelativeLevel <= 0)
        {
            return;
        }

        var minAbsoluteLevel = node.LevelNumber;
        var maxAbsoluteLevel = node.LevelNumber + maxRelativeLevel;

        var cap = await _dialogService.ShowLevelCapDialogAsync(minAbsoluteLevel, maxAbsoluteLevel);
        if (cap is not { } absoluteLevel)
        {
            return;
        }

        PushUndoSnapshot();
        TruncateBelowLevel(node, absoluteLevel - node.LevelNumber);
        node.SyncChildOrderToModel();
        await RecomputeAllPageNumberDisplaysAsync();
    }

    private static void TruncateBelowLevel(BookmarkNodeViewModel node, int remainingLevels)
    {
        if (remainingLevels <= 0)
        {
            node.Children.Clear();
            node.Model.Children.Clear();
            return;
        }

        foreach (var child in node.Children)
        {
            TruncateBelowLevel(child, remainingLevels - 1);
        }
    }

    /// <summary>
    /// 結合用のブックマークツリーを返す。ForceFitForAllがオンの場合、元のデータは変更せず、
    /// 全ノードの表示方法をFitに差し替えた複製を返す。
    /// </summary>
    public IReadOnlyList<BookmarkNode> ToModel() =>
        ForceFitForAll.Value ? _rootModel.Select(CloneWithFit).ToList() : _rootModel;

    /// <summary>
    /// しおり設定ファイル書き出し用のブックマークツリーを返す(非破壊な複製)。各ノードのPageOffsetに、
    /// 自身の編集分だけでなく、結合順で前にあるファイルの編集による結合後ページ数の連鎖分も
    /// 合算して反映する。ForceFitForAllがオンの場合はToModel()と同様にFit一律の複製にする。
    /// </summary>
    public IReadOnlyList<BookmarkNode> ToExportModel()
    {
        var cumulativeBeforeFile = ComputeCumulativeDeltaBeforeFile();

        BookmarkNode CloneForExport(BookmarkNode node)
        {
            var crossFileDelta = cumulativeBeforeFile.GetValueOrDefault(node.SourceFileEntryId, 0);
            var clone = new BookmarkNode
            {
                SourceFileEntryId = node.SourceFileEntryId,
                OriginalPageIndex = node.OriginalPageIndex,
                MergedPageIndex = node.MergedPageIndex,
                Title = node.Title,
                DestinationType = ForceFitForAll.Value ? BookmarkDestinationType.Fit : node.DestinationType,
                Left = ForceFitForAll.Value ? null : node.Left,
                Top = ForceFitForAll.Value ? null : node.Top,
                Right = ForceFitForAll.Value ? null : node.Right,
                Bottom = ForceFitForAll.Value ? null : node.Bottom,
                Zoom = ForceFitForAll.Value ? null : node.Zoom,
                PageOffset = (node.PageOffset ?? 0) + crossFileDelta,
                IsOpen = node.IsOpen,
            };
            clone.Children.AddRange(node.Children.Select(CloneForExport));
            return clone;
        }

        return _rootModel.Select(CloneForExport).ToList();
    }

    private static BookmarkNode CloneWithFit(BookmarkNode node)
    {
        var clone = new BookmarkNode
        {
            SourceFileEntryId = node.SourceFileEntryId,
            OriginalPageIndex = node.OriginalPageIndex,
            MergedPageIndex = node.MergedPageIndex,
            Title = node.Title,
            DestinationType = BookmarkDestinationType.Fit,
            PageOffset = node.PageOffset,
            IsOpen = node.IsOpen,
        };
        clone.Children.AddRange(node.Children.Select(CloneWithFit));
        return clone;
    }

    /// <summary>
    /// しおり設定画面で結合前ページ数のテキストボックスが編集された際に呼ばれる。
    /// 同一ファイル内で、編集されたノードの元となるPDFページ位置(OriginalPageIndex)以降にある
    /// 全ノード(しおりツリー上の順序ではなく、あくまでPDFのページ構造上の位置基準)へ、
    /// 差分(delta)を一律に加算する。
    /// </summary>
    private void OnPreOffsetPageNumberChanged(BookmarkNodeViewModel node, int newValue)
    {
        if (_isRecomputingPageNumbers)
        {
            return;
        }

        var oldValue = node.Model.OriginalPageIndex + 1 + (node.Model.PageOffset ?? 0);
        var delta = newValue - oldValue;
        if (delta == 0)
        {
            return;
        }

        PushUndoSnapshot($"{node.Model.Id}:PreOffsetPageNumber");

        var fileId = node.Model.SourceFileEntryId;
        var pivotOriginalIndex = node.Model.OriginalPageIndex;
        WalkAll(RootNodes, vm =>
        {
            if (vm.Model.SourceFileEntryId == fileId && vm.Model.OriginalPageIndex >= pivotOriginalIndex)
            {
                vm.Model.PageOffset = (vm.Model.PageOffset ?? 0) + delta;
            }
        });

        TriggerRecompute();
    }

    /// <summary>
    /// 指定ノードが属するPDFファイル(SourceFileEntryId)に関係する結合前ページ数の編集を、
    /// そのファイル内の全ノードについて一括でリセットする(PageOffsetを未編集状態=nullへ戻す)。
    /// 個々のノード単位でのリセット(そのノードのOriginalPageIndex以降のみ戻す)では、
    /// 編集対象ノードより前のページに及ぼした過去の編集が残ってしまうため、
    /// ファイル単位で完全に元へ戻す。
    /// </summary>
    public void ResetFilePageNumbers(BookmarkNodeViewModel node)
    {
        var fileId = node.Model.SourceFileEntryId;
        var hasEdits = false;
        WalkAll(RootNodes, vm =>
        {
            if (vm.Model.SourceFileEntryId == fileId && (vm.Model.PageOffset ?? 0) != 0)
            {
                hasEdits = true;
            }
        });

        if (!hasEdits)
        {
            return;
        }

        PushUndoSnapshot();
        WalkAll(RootNodes, vm =>
        {
            if (vm.Model.SourceFileEntryId == fileId)
            {
                vm.Model.PageOffset = null;
            }
        });

        TriggerRecompute();
    }

    /// <summary>
    /// RecomputeAllPageNumberDisplaysAsyncを実行して結果を待たず、呼び出し元(構造編集メソッド群)へ
    /// 即座に制御を返す。ノード数の少ないツリーでは内部で一度もawaitが発生しないため、このメソッドから
    /// 戻った時点で実質的に処理は完了している(既存の同期呼び出し前提のテスト・コードビハインドは無改修で動く)。
    /// ノード数が多いツリーでは内部でIsBusy/BusyProgressを更新しながらUIスレッドへ制御を返しつつ進行するため、
    /// 呼び出し元がその完了を待つ必要はない(結果はReactivePropertySlim経由でUIへ反映される)。
    /// 例外はasync voidの通例どおりSynchronizationContext経由でアプリ全体の未処理例外ハンドラに届く。
    /// </summary>
    private async void TriggerRecompute() => await RecomputeAllPageNumberDisplaysAsync();

    /// <summary>
    /// 全ノードのPreOffsetPageNumber/DisplayMergedPageNumberを、現在のPageOffset設定に基づいて
    /// 再計算し各ノードへ書き戻す。あわせてHasPageNumberEdits/HasPageNumberInconsistencyも更新する。
    /// ツリー構造・PageOffsetを変更しうるすべての操作(読込・Undo・追加・削除・移動・編集)の後に呼ぶ。
    /// しおりが大量にある場合、この処理(特にノードごとのプロパティ書き戻しに伴うUIバインディング更新)は
    /// UIスレッドを長時間占有しうる。RecomputeChunkSize件処理するごとにawait Task.Yield()で制御を返し、
    /// その間はIsBusy/BusyProgressで進捗を報告する(MainWindowViewModel経由で既存の処理中オーバーレイに
    /// 反映される)。このオーバーレイはしおり編集画面(手順2/3)全体を覆いマウス操作を受け付けなくなるため、
    /// 実行中に本メソッドの別呼び出しが(ユーザー操作起点で)重ねて発生することは想定していない。
    /// このViewModel自身が書き戻す値(PreOffsetPageNumber等)からの再帰は_isRecomputingPageNumbersで防ぐ。
    /// internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。
    /// </summary>
    internal async Task RecomputeAllPageNumberDisplaysAsync()
    {
        _isRecomputingPageNumbers = true;
        try
        {
            var cumulativeBeforeFile = ComputeCumulativeDeltaBeforeFile();

            var allNodes = new List<BookmarkNodeViewModel>();
            WalkAll(RootNodes, allNodes.Add);
            var total = allNodes.Count;
            var showProgress = total > RecomputeChunkSize;

            if (showProgress)
            {
                IsBusy.Value = true;
                BusyProgress.Value = new BusyProgressInfo(0, total, []);
                await Task.Yield();
            }

            var hasEdits = false;
            var hasInconsistency = false;
            for (var i = 0; i < total; i++)
            {
                var vm = allNodes[i];
                var offset = vm.Model.PageOffset ?? 0;
                vm.IsPageNumberEdited.Value = offset != 0;
                if (offset != 0)
                {
                    hasEdits = true;
                }

                var preNumber = vm.Model.OriginalPageIndex + 1 + offset;
                vm.PreOffsetPageNumber.Value = preNumber;
                if (preNumber < 1)
                {
                    hasInconsistency = true;
                }

                if (vm.MergedPageNumber is { } baseMergedNumber)
                {
                    var crossFileDelta = cumulativeBeforeFile.GetValueOrDefault(vm.Model.SourceFileEntryId, 0);
                    var mergedNumber = baseMergedNumber + crossFileDelta + offset;
                    vm.DisplayMergedPageNumber.Value = mergedNumber;
                    if (mergedNumber < 1)
                    {
                        hasInconsistency = true;
                    }
                }

                if (showProgress && (i + 1) % RecomputeChunkSize == 0)
                {
                    BusyProgress.Value = new BusyProgressInfo(i + 1, total, []);
                    await Task.Yield();
                }
            }

            HasPageNumberEdits.Value = hasEdits;
            HasPageNumberInconsistency.Value = hasInconsistency;
        }
        finally
        {
            _isRecomputingPageNumbers = false;
            if (IsBusy.Value)
            {
                IsBusy.Value = false;
                BusyProgress.Value = null;
            }
        }
    }

    /// <summary>
    /// ファイルごとの「そのファイル全体に効く累積差分(FileTotalDelta)」を、結合順(_orderedFileIds)に
    /// 沿って積み上げ、各ファイルについて「自分より前にあるファイルの累積差分の合計」を返す。
    /// FileTotalDeltaは、そのファイル内で最もOriginalPageIndexが大きいノードのPageOffsetに等しい
    /// (どの編集も自身のOriginalPageIndex以降=最終ページを含む範囲に及ぶため、最終ページのノードは
    /// そのファイルに対するすべての編集の差分を合算した値を持つことになる)。
    /// </summary>
    private Dictionary<Guid, int> ComputeCumulativeDeltaBeforeFile()
    {
        var maxIndexPerFile = new Dictionary<Guid, int>();
        var fileTotalDelta = new Dictionary<Guid, int>();
        WalkAll(RootNodes, vm =>
        {
            var fileId = vm.Model.SourceFileEntryId;
            if (!maxIndexPerFile.TryGetValue(fileId, out var currentMax) || vm.Model.OriginalPageIndex > currentMax)
            {
                maxIndexPerFile[fileId] = vm.Model.OriginalPageIndex;
                fileTotalDelta[fileId] = vm.Model.PageOffset ?? 0;
            }
        });

        var cumulativeBeforeFile = new Dictionary<Guid, int>();
        var running = 0;
        foreach (var fileId in _orderedFileIds)
        {
            cumulativeBeforeFile[fileId] = running;
            running += fileTotalDelta.GetValueOrDefault(fileId, 0);
        }

        return cumulativeBeforeFile;
    }

    private static void WalkAll(IEnumerable<BookmarkNodeViewModel> nodes, Action<BookmarkNodeViewModel> action)
    {
        foreach (var node in nodes)
        {
            action(node);
            WalkAll(node.Children, action);
        }
    }

    public BookmarkNodeViewModel AddRoot()
    {
        var (fileId, pageIndex) = ResolveDefaultDestination();
        PushUndoSnapshot();
        var model = new BookmarkNode { SourceFileEntryId = fileId, OriginalPageIndex = pageIndex, Title = Strings.NewBookmarkDefaultTitle };
        var vm = new BookmarkNodeViewModel(model, _fileNames.GetValueOrDefault(fileId, "?"), null, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot, OnPreOffsetPageNumberChanged);
        ApplyCurrentOverridesToNewNode(vm);

        RootNodes.Add(vm);
        _rootModel.Add(model);
        TriggerRecompute();
        return vm;
    }

    public BookmarkNodeViewModel AddChild(BookmarkNodeViewModel parent)
    {
        PushUndoSnapshot();
        var model = new BookmarkNode
        {
            SourceFileEntryId = parent.Model.SourceFileEntryId,
            OriginalPageIndex = parent.Model.OriginalPageIndex,
            Title = Strings.NewBookmarkDefaultTitle,
            MergedPageIndex = parent.Model.MergedPageIndex,
            // 親と同じ元ページ(OriginalPageIndex)を指すため、そのページに既に適用されている
            // 結合前ページ数のオフセットも引き継ぐ(同じページを指す行同士で表示が食い違わないように)。
            PageOffset = parent.Model.PageOffset,
        };
        var vm = new BookmarkNodeViewModel(model, parent.SourceFileName, parent, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot, OnPreOffsetPageNumberChanged);
        ApplyCurrentOverridesToNewNode(vm);

        parent.Children.Add(vm);
        parent.IsExpanded.Value = true;
        parent.SyncChildOrderToModel();
        TriggerRecompute();
        return vm;
    }

    public BookmarkNodeViewModel AddSiblingAfter(BookmarkNodeViewModel reference)
    {
        PushUndoSnapshot();
        var model = new BookmarkNode
        {
            SourceFileEntryId = reference.Model.SourceFileEntryId,
            OriginalPageIndex = reference.Model.OriginalPageIndex,
            Title = Strings.NewBookmarkDefaultTitle,
            MergedPageIndex = reference.Model.MergedPageIndex,
            // 参照元と同じ元ページ(OriginalPageIndex)を指すため、そのページに既に適用されている
            // 結合前ページ数のオフセットも引き継ぐ(同じページを指す行同士で表示が食い違わないように)。
            PageOffset = reference.Model.PageOffset,
        };
        var vm = new BookmarkNodeViewModel(model, reference.SourceFileName, reference.Parent, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot, OnPreOffsetPageNumberChanged);
        ApplyCurrentOverridesToNewNode(vm);

        var siblings = reference.Parent?.Children ?? RootNodes;
        var index = siblings.IndexOf(reference);
        siblings.Insert(index + 1, vm);

        if (reference.Parent is null)
        {
            var modelIndex = _rootModel.IndexOf(reference.Model);
            _rootModel.Insert(modelIndex + 1, model);
        }
        else
        {
            reference.Parent.SyncChildOrderToModel();
        }

        TriggerRecompute();
        return vm;
    }

    public void Remove(BookmarkNodeViewModel node)
    {
        PushUndoSnapshot();
        var collection = node.Parent?.Children ?? RootNodes;
        collection.Remove(node);

        if (node.Parent is null)
        {
            _rootModel.Remove(node.Model);
        }
        else
        {
            node.Parent.SyncChildOrderToModel();
        }

        TriggerRecompute();
    }

    /// <summary>ノードをnewParent(nullの場合はルート)のnewIndex位置へ移動する。ドラッグ&ドロップ並べ替え用。</summary>
    public void Move(BookmarkNodeViewModel node, BookmarkNodeViewModel? newParent, int newIndex)
    {
        if (node == newParent || IsDescendantOf(newParent, node))
        {
            return;
        }

        PushUndoSnapshot();
        var oldParent = node.Parent;
        var oldCollection = oldParent?.Children ?? RootNodes;
        var newCollectionBeforeRemoval = newParent?.Children ?? RootNodes;

        // 同一コレクション内での移動時、移動対象が挿入先より前にある場合は
        // 削除によって後続要素のインデックスが1つ前にずれるため、挿入先も補正する。
        if (ReferenceEquals(oldCollection, newCollectionBeforeRemoval))
        {
            var oldIndex = oldCollection.IndexOf(node);
            if (oldIndex >= 0 && oldIndex < newIndex)
            {
                newIndex--;
            }
        }

        oldCollection.Remove(node);

        if (oldParent is null)
        {
            _rootModel.Remove(node.Model);
        }
        else
        {
            oldParent.SyncChildOrderToModel();
        }

        node.Parent = newParent;
        var newCollection = newParent?.Children ?? RootNodes;
        newIndex = Math.Clamp(newIndex, 0, newCollection.Count);
        newCollection.Insert(newIndex, node);

        if (newParent is null)
        {
            _rootModel.Insert(newIndex, node.Model);
        }
        else
        {
            newParent.SyncChildOrderToModel();
            newParent.IsExpanded.Value = true;
        }
    }

    /// <summary>ノードのレベルを1つ上げられるか(親を持つか)。ルート直下の要素は既に最上位のためfalse。</summary>
    public bool CanPromoteLevel(BookmarkNodeViewModel node) => node.Parent is not null;

    /// <summary>
    /// ノードのレベルを1つ下げられるか。レベルを下げる操作は「直前の兄弟の末尾の子」として
    /// 再配置することで実現するため、直前の兄弟が存在しない(先頭要素の)場合はfalse。
    /// </summary>
    public bool CanDemoteLevel(BookmarkNodeViewModel node)
    {
        var siblings = node.Parent?.Children ?? RootNodes;
        return siblings.IndexOf(node) > 0;
    }

    /// <summary>
    /// ノードのレベルを1つ上げる(=階層を1つ浅くする)。元の親の直後の兄弟として再配置する。
    /// ルート直下の要素(親を持たない)の場合は何もしない。
    /// </summary>
    public void PromoteLevel(BookmarkNodeViewModel node)
    {
        if (node.Parent is not { } oldParent)
        {
            return;
        }

        var grandParent = oldParent.Parent;
        var grandParentCollection = grandParent?.Children ?? RootNodes;
        var newIndex = grandParentCollection.IndexOf(oldParent) + 1;
        Move(node, grandParent, newIndex);
    }

    /// <summary>
    /// ノードのレベルを1つ下げる(=階層を1つ深くする)。直前の兄弟の末尾の子として再配置する。
    /// 直前の兄弟が存在しない(先頭要素の)場合は何もしない。
    /// </summary>
    public void DemoteLevel(BookmarkNodeViewModel node)
    {
        var siblings = node.Parent?.Children ?? RootNodes;
        var index = siblings.IndexOf(node);
        if (index <= 0)
        {
            return;
        }

        var newParent = siblings[index - 1];
        Move(node, newParent, newParent.Children.Count);
    }

    private static bool IsDescendantOf(BookmarkNodeViewModel? candidateDescendant, BookmarkNodeViewModel node)
    {
        var current = candidateDescendant;
        while (current is not null)
        {
            if (current == node)
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private (Guid FileId, int PageIndex) ResolveDefaultDestination()
    {
        if (RootNodes.Count > 0)
        {
            return (RootNodes[0].Model.SourceFileEntryId, 0);
        }

        if (_fileNames.Count > 0)
        {
            return (_fileNames.Keys.First(), 0);
        }

        throw new InvalidOperationException(Strings.NoMergeTargetFilesError);
    }
}
