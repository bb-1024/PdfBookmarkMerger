using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
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
    /// 新規ドキュメントの読み込み。前のドキュメントのUndo履歴は引き継がない(クリアする)。
    /// </summary>
    public void Load(IReadOnlyList<BookmarkNode> rootBookmarks, IReadOnlyDictionary<Guid, string> fileNames)
    {
        _fileNames = fileNames;
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
            RootNodes.Add(new BookmarkNodeViewModel(node, name, null, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot));
        }
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

    private static BookmarkNode CloneWithFit(BookmarkNode node)
    {
        var clone = new BookmarkNode
        {
            SourceFileEntryId = node.SourceFileEntryId,
            OriginalPageIndex = node.OriginalPageIndex,
            MergedPageIndex = node.MergedPageIndex,
            Title = node.Title,
            DestinationType = BookmarkDestinationType.Fit,
            IsOpen = node.IsOpen,
        };
        clone.Children.AddRange(node.Children.Select(CloneWithFit));
        return clone;
    }

    public BookmarkNodeViewModel AddRoot()
    {
        var (fileId, pageIndex) = ResolveDefaultDestination();
        PushUndoSnapshot();
        var model = new BookmarkNode { SourceFileEntryId = fileId, OriginalPageIndex = pageIndex, Title = "新しいしおり" };
        var vm = new BookmarkNodeViewModel(model, _fileNames.GetValueOrDefault(fileId, "?"), null, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot);
        ApplyCurrentOverridesToNewNode(vm);

        RootNodes.Add(vm);
        _rootModel.Add(model);
        return vm;
    }

    public BookmarkNodeViewModel AddChild(BookmarkNodeViewModel parent)
    {
        PushUndoSnapshot();
        var model = new BookmarkNode
        {
            SourceFileEntryId = parent.Model.SourceFileEntryId,
            OriginalPageIndex = parent.Model.OriginalPageIndex,
            Title = "新しいしおり",
            MergedPageIndex = parent.Model.MergedPageIndex,
        };
        var vm = new BookmarkNodeViewModel(model, parent.SourceFileName, parent, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot);
        ApplyCurrentOverridesToNewNode(vm);

        parent.Children.Add(vm);
        parent.IsExpanded.Value = true;
        parent.SyncChildOrderToModel();
        return vm;
    }

    public BookmarkNodeViewModel AddSiblingAfter(BookmarkNodeViewModel reference)
    {
        PushUndoSnapshot();
        var model = new BookmarkNode
        {
            SourceFileEntryId = reference.Model.SourceFileEntryId,
            OriginalPageIndex = reference.Model.OriginalPageIndex,
            Title = "新しいしおり",
            MergedPageIndex = reference.Model.MergedPageIndex,
        };
        var vm = new BookmarkNodeViewModel(model, reference.SourceFileName, reference.Parent, ForceFitForAll, GlobalExpandOverride, PushUndoSnapshot);
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

        throw new InvalidOperationException("結合対象ファイルがありません。");
    }
}
