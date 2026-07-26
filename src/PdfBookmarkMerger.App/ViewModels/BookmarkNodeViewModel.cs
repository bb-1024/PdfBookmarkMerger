using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using PdfBookmarkMerger.Core.Models;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// しおりツリーの1ノード分のViewModel。Title/表示方法/Open状態/座標を編集可能なプロパティとして公開する。
/// </summary>
public sealed class BookmarkNodeViewModel : ViewModelBase
{
    public BookmarkNodeViewModel(
        BookmarkNode model,
        string sourceFileName,
        BookmarkNodeViewModel? parent,
        IObservable<bool> forceFitForAll,
        IObservable<bool?> globalExpandOverride)
    {
        Model = model;
        SourceFileName = sourceFileName;
        Parent = parent;

        Title = new ReactivePropertySlim<string>(model.Title).AddTo(Disposables);
        Title.Subscribe(v => model.Title = v).AddTo(Disposables);

        IsOpen = new ReactivePropertySlim<bool>(model.IsOpen).AddTo(Disposables);
        IsOpen.Subscribe(v => model.IsOpen = v).AddTo(Disposables);

        DestinationType = new ReactivePropertySlim<BookmarkDestinationType>(model.DestinationType).AddTo(Disposables);
        DestinationType.Subscribe(v => model.DestinationType = v).AddTo(Disposables);

        Left = new ReactivePropertySlim<double?>(model.Left).AddTo(Disposables);
        Left.Subscribe(v => model.Left = v).AddTo(Disposables);

        Top = new ReactivePropertySlim<double?>(model.Top).AddTo(Disposables);
        Top.Subscribe(v => model.Top = v).AddTo(Disposables);

        Right = new ReactivePropertySlim<double?>(model.Right).AddTo(Disposables);
        Right.Subscribe(v => model.Right = v).AddTo(Disposables);

        Bottom = new ReactivePropertySlim<double?>(model.Bottom).AddTo(Disposables);
        Bottom.Subscribe(v => model.Bottom = v).AddTo(Disposables);

        Zoom = new ReactivePropertySlim<double?>(model.Zoom).AddTo(Disposables);
        Zoom.Subscribe(v => model.Zoom = v).AddTo(Disposables);

        IsExpanded = new ReactivePropertySlim<bool>(true).AddTo(Disposables);

        // 表示方法(DestinationType)に応じて、実際にPDFへ反映される座標コントロールのみ活性化する。
        // 「一律でFitに設定」がオンの場合は、表示方法・座標コントロールともに一律で不活性化する。
        IsDestinationTypeEditable = forceFitForAll.Select(forced => !forced)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);
        IsLeftEditable = DestinationType.CombineLatest(forceFitForAll,
                (type, forced) => !forced && type is BookmarkDestinationType.XYZ or BookmarkDestinationType.FitV)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);
        IsTopEditable = DestinationType.CombineLatest(forceFitForAll,
                (type, forced) => !forced && type is BookmarkDestinationType.XYZ or BookmarkDestinationType.FitH)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);
        IsZoomEditable = DestinationType.CombineLatest(forceFitForAll,
                (type, forced) => !forced && type is BookmarkDestinationType.XYZ)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);

        Children = new ObservableCollection<BookmarkNodeViewModel>(
            model.Children.Select(c => new BookmarkNodeViewModel(c, sourceFileName, this, forceFitForAll, globalExpandOverride)));

        // 最下位(子を持たない)要素は展開/折りたたみの区別に意味がないため編集不可にする。
        // 「一律で展開表示を設定」がON/OFF(非null)の間も、個別の展開表示チェックボックスは編集不可にする。
        var hasChildren = Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Children.CollectionChanged += h,
                h => Children.CollectionChanged -= h)
            .Select(_ => Children.Count > 0)
            .StartWith(Children.Count > 0);
        IsExpandToggleEditable = globalExpandOverride.CombineLatest(hasChildren,
                (overrideValue, children) => overrideValue is null && children)
            .ToReadOnlyReactivePropertySlim().AddTo(Disposables);
    }

    public BookmarkNode Model { get; }

    public BookmarkNodeViewModel? Parent { get; set; }

    public string SourceFileName { get; }

    public int OriginalPageIndex => Model.OriginalPageIndex;

    /// <summary>結合前ファイル内での1始まりページ番号(表示用)。</summary>
    public int OriginalPageNumber => Model.OriginalPageIndex + 1;

    /// <summary>結合後PDFにおける1始まりページ番号(表示用)。</summary>
    public int? MergedPageNumber => Model.MergedPageIndex is { } idx ? idx + 1 : null;

    /// <summary>ツリー上の階層の深さ(ルート=0)。列の縦位置揃えに使う。</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>自身の階層レベル(ルート=1始まり、表示用)。</summary>
    public int LevelNumber => Depth + 1;

    public string ActionType => Model.ActionType;

    public ReactivePropertySlim<string> Title { get; }

    public ReactivePropertySlim<bool> IsOpen { get; }

    public ReactivePropertySlim<BookmarkDestinationType> DestinationType { get; }

    public ReactivePropertySlim<double?> Left { get; }

    public ReactivePropertySlim<double?> Top { get; }

    public ReactivePropertySlim<double?> Right { get; }

    public ReactivePropertySlim<double?> Bottom { get; }

    public ReactivePropertySlim<double?> Zoom { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; }

    public ObservableCollection<BookmarkNodeViewModel> Children { get; }

    /// <summary>表示方法(ComboBox)を編集可能か。「一律でFitに設定」がオンの間はfalse。</summary>
    public ReadOnlyReactivePropertySlim<bool> IsDestinationTypeEditable { get; }

    /// <summary>Left座標を編集可能か(XYZ/FitVのみ)。</summary>
    public ReadOnlyReactivePropertySlim<bool> IsLeftEditable { get; }

    /// <summary>Top座標を編集可能か(XYZ/FitHのみ)。</summary>
    public ReadOnlyReactivePropertySlim<bool> IsTopEditable { get; }

    /// <summary>Zoomを編集可能か(XYZのみ)。</summary>
    public ReadOnlyReactivePropertySlim<bool> IsZoomEditable { get; }

    /// <summary>展開表示チェックボックスを編集可能か。子を持たない(最下位)場合、
    /// または「一律で展開表示を設定」がON/OFFの間はfalse。</summary>
    public ReadOnlyReactivePropertySlim<bool> IsExpandToggleEditable { get; }

    /// <summary>編集結果をModel.Childrenの順序に反映する(D&D並べ替え後の同期用)。</summary>
    public void SyncChildOrderToModel()
    {
        Model.Children.Clear();
        Model.Children.AddRange(Children.Select(c => c.Model));
    }
}
