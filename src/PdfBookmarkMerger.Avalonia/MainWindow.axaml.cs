using System.Collections.Specialized;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.AvaloniaApp;

public partial class MainWindow : Window
{
    private static readonly DataFormat<PdfFileEntryViewModel> FileDragFormat =
        DataFormat.CreateInProcessFormat<PdfFileEntryViewModel>("PdfBookmarkMerger.FileEntry");

    private static readonly DataFormat<BookmarkNodeViewModel> BookmarkDragFormat =
        DataFormat.CreateInProcessFormat<BookmarkNodeViewModel>("PdfBookmarkMerger.BookmarkNode");

    /// <summary>しおり行のドロップ判定で、子として挿入する場合のインジケータ線のインデント量(px)。</summary>
    private const double BookmarkChildIndent = 19;

    private readonly record struct BookmarkDropPlan(BookmarkNodeViewModel TargetNode, bool InsertAsChild, double LineX, double LineY);

    private PointerPressedEventArgs? _filePressedArgs;
    private PdfFileEntryViewModel? _filePressedEntry;
    private bool _fileDragInProgress;

    private PointerPressedEventArgs? _bookmarkPressedArgs;
    private BookmarkNodeViewModel? _bookmarkPressedNode;
    private bool _bookmarkDragInProgress;

    /// <summary>IsBusyが5秒以上継続した場合にのみ、詳細進捗(BusyDetailText)を表示するためのタイマー。</summary>
    private readonly DispatcherTimer _busyDetailTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>D&D中、この余白(px)以内にカーソルが入るとツリーの自動スクロールを開始する。</summary>
    private const double AutoScrollEdgeMargin = 32;

    /// <summary>自動スクロールの1ティックあたりの移動量(px)。</summary>
    private const double AutoScrollStep = 18;

    private ScrollViewer? _bookmarkTreeScrollViewer;
    private readonly DispatcherTimer _bookmarkAutoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private double _bookmarkAutoScrollStep;

    /// <summary>言語切り替え時にウィンドウが差し替えられても、同じViewModelインスタンスへの
    /// 購読が古いウィンドウに残り続けないよう、Closedで一括破棄する。</summary>
    private readonly CompositeDisposable _viewModelSubscriptions = [];

    /// <summary>trueの間は、ユーザーの手動スクロールに追従してCurrentPageIndexを更新している最中であることを示す。
    /// この間はCurrentPageIndexの変更を受けてもプレビューのスクロール位置を動かし直さない
    /// (動かすと、追従した瞬間に別の位置へジャンプしてしまいスクロールが成立しなくなる)。</summary>
    private bool _isSyncingCurrentPageFromScroll;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        // しおり編集画面に入るたびに、タイトル列の幅を現在のタイトル群に合わせて再計算する。
        ViewModel.Step.Subscribe(step =>
        {
            if (step == WorkflowStep.EditBookmarks)
            {
                RecomputeTitleColumnWidth();
            }
        }).AddTo(_viewModelSubscriptions);

        _busyDetailTimer.Tick += OnBusyDetailTimerTick;
        ViewModel.IsBusy.Subscribe(OnIsBusyChanged).AddTo(_viewModelSubscriptions);

        // Undo(元に戻す)はRootNodes全体を作り直すため、タイトル列幅もあわせて再計算する。
        // VM側のUndoCommand.Subscribe(Undo)(RootNodes再構築)が先に完了してから呼ばれる。
        ViewModel.BookmarkTree.UndoCommand.Subscribe(RecomputeTitleColumnWidth).AddTo(_viewModelSubscriptions);

        _bookmarkAutoScrollTimer.Tick += (_, _) =>
        {
            if (_bookmarkTreeScrollViewer is not { } scrollViewer)
            {
                return;
            }

            var offset = scrollViewer.Offset;
            var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var newY = Math.Clamp(offset.Y + _bookmarkAutoScrollStep, 0, maxY);
            scrollViewer.Offset = new Vector(offset.X, newY);
        };

        ViewModel.FileList.Files.CollectionChanged += OnFileListFilesCollectionChanged;
        UpdateFileMoveButtonsEnabled();

        // リンクのホットスポット表示は、対象ページ・拡大率・ページ高さ(座標変換の基準)のいずれかが
        // 変わるたびに再計算が必要。リンク自体の追加・削除でも当然再描画する。
        ViewModel.LinkEditor.Links.CollectionChanged += OnLinkEditorLinksCollectionChanged;
        ViewModel.LinkEditor.ZoomScale.Subscribe(_ => RedrawLinkOverlay()).AddTo(_viewModelSubscriptions);
        ViewModel.LinkEditor.PageHeight.Subscribe(_ => RedrawLinkOverlay()).AddTo(_viewModelSubscriptions);

        // ページ送りボタン・しおりジャンプ等、ユーザーの手動スクロール以外の経路でCurrentPageIndexが
        // 変わった時だけ、そのページの先頭が見える位置までスクロールする(_isSyncingCurrentPageFromScroll中は、
        // 逆にスクロール操作がCurrentPageIndexを追従させただけなので、スクロール位置を動かし直さない)。
        ViewModel.LinkEditor.CurrentPageIndex.Subscribe(pageIndex =>
        {
            if (_isSyncingCurrentPageFromScroll)
            {
                RedrawLinkOverlay();
                return;
            }

            Dispatcher.UIThread.Post(() => ScrollToPage(pageIndex), DispatcherPriority.Loaded);
        }).AddTo(_viewModelSubscriptions);

        // ListBox/TreeView自身の選択処理(SelectingItemsControlの既定の内部処理)がPointerPressedを
        // Bubbleフェーズで先取りしてHandled=trueにするため、通常のXAMLイベント購読(Bubble)では
        // D&D開始検知用のハンドラに一切イベントが届かない。WPF版がPreviewMouseLeftButtonDown
        // (Tunnelフェーズ)を使っているのと同様、Tunnelフェーズで明示的に購読することで、
        // 既定の選択処理より先に(確実に)イベントを受け取れるようにする。
        FileListBox.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        BookmarkTreeView.AddHandler(PointerPressedEvent, OnBookmarkTreePointerPressed, RoutingStrategies.Tunnel);

        Closed += OnMainWindowClosed;
    }

    private void OnFileListFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateFileMoveButtonsEnabled();

    /// <summary>
    /// 言語切り替え時、このウィンドウはViewModelより先に破棄される(AvaloniaDialogService.
    /// ReplaceMainWindowForLanguageChangeが新ウィンドウへ同じViewModelを引き継ぐため)。
    /// ここで購読を解除しないと、古いウィンドウのコールバックがViewModelの変化のたびに
    /// (既に閉じた)自分自身のUI要素を触り続けてしまう。
    /// </summary>
    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        Closed -= OnMainWindowClosed;
        ViewModel.FileList.Files.CollectionChanged -= OnFileListFilesCollectionChanged;
        ViewModel.LinkEditor.Links.CollectionChanged -= OnLinkEditorLinksCollectionChanged;
        _viewModelSubscriptions.Dispose();
    }

    private void OnLinkEditorLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawLinkOverlay();

    private void OnFileListSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateFileMoveButtonsEnabled();

    /// <summary>
    /// 選択中のファイルがリストの最上部/最下部にあり、それ以上その方向へ移動できない場合に、
    /// 対応する「上へ」「下へ」ボタンを非活性化する。何も選択されていない場合は両方とも非活性化する。
    /// </summary>
    private void UpdateFileMoveButtonsEnabled()
    {
        var selected = FileListBox.SelectedItems!.Cast<PdfFileEntryViewModel>().ToList();
        var (canMoveUp, canMoveDown) = ViewModel.FileList.GetMoveAvailability(selected);
        MoveFileUpButton.IsEnabled = canMoveUp;
        MoveFileDownButton.IsEnabled = canMoveDown;
    }

    private void OnIsBusyChanged(bool isBusy)
    {
        BusyDetailText.IsVisible = false;
        _busyDetailTimer.Stop();
        if (isBusy)
        {
            _busyDetailTimer.Start();
        }
    }

    private void OnBusyDetailTimerTick(object? sender, EventArgs e)
    {
        _busyDetailTimer.Stop();
        BusyDetailText.IsVisible = true;
    }

    public MainWindowViewModel ViewModel { get; }

    // ---- しおりツリー: タイトル列の幅をタイトル文字列の実測幅に追従させる ----

    private void OnBookmarkTitleTextChanged(object? sender, TextChangedEventArgs e) => RecomputeTitleColumnWidth();

    /// <summary>
    /// 現在のしおりツリー全ノードのタイトルを実測し、最も幅を必要とするノードに合わせて
    /// タイトル列の共有基準幅(BookmarkTree.TitleColumnBaseWidth)を更新する。
    /// </summary>
    private void RecomputeTitleColumnWidth()
    {
        // DepthToTitleWidthConverter.IndentPerLevelと同じ値(AvaloniaのTreeViewItemが実際に
        // 1階層あたり適用するインデント幅)にする必要がある。ずれると列がわずかに崩れる。
        const double indentPerLevel = 16;
        const double horizontalPadding = 16;

        var required = BookmarkTreeViewModel.DefaultTitleColumnBaseWidth;

        void Walk(IEnumerable<BookmarkNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                var textWidth = MeasureTextWidth(node.Title.Value);
                var candidate = textWidth + horizontalPadding + (node.Depth * indentPerLevel);
                if (candidate > required)
                {
                    required = candidate;
                }

                Walk(node.Children);
            }
        }

        Walk(ViewModel.BookmarkTree.RootNodes);
        ViewModel.BookmarkTree.TitleColumnBaseWidth.Value = required;
    }

    /// <summary>
    /// TextBoxの既定フォント(Segoe UIではなく、Program.csの.WithInterFont()で既定化したInter、
    /// 既定サイズ14pt = FluentThemeのControlContentThemeFontSize)と合わせて実測する。
    /// ここがTextBoxの実際の描画と食い違うと、タイトル列の幅計算がずれて縦位置が揃わなくなる。
    /// </summary>
    private static double MeasureTextWidth(string text)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            Brushes.Black);
        return formattedText.Width;
    }

    // ---- ウィンドウ全体へのD&D (OSのファイルマネージャーからのファイル/フォルダ) ----

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (ViewModel.Step.Value == WorkflowStep.SelectFiles && e.DataTransfer.TryGetFiles() is { Length: > 0 })
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel.Step.Value != WorkflowStep.SelectFiles)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is not { Length: > 0 })
        {
            return;
        }

        ViewModel.FileList.AddPaths(files.Select(f => f.Path.LocalPath));
    }

    // ---- ファイル一覧: 内部D&Dによる並べ替え ----

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
        _filePressedEntry = item?.DataContext as PdfFileEntryViewModel;
        _filePressedArgs = _filePressedEntry is not null ? e : null;
    }

    private async void OnFileListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_fileDragInProgress || _filePressedEntry is null || _filePressedArgs is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(FileListBox).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dragged = _filePressedEntry;
        var pressArgs = _filePressedArgs;
        _fileDragInProgress = true;
        _filePressedEntry = null;
        _filePressedArgs = null;

        try
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(FileDragFormat, dragged));
            await DragDrop.DoDragDropAsync(pressArgs, dataTransfer, DragDropEffects.Move);
        }
        finally
        {
            _fileDragInProgress = false;
        }
    }

    private void OnFileListDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(FileDragFormat))
        {
            e.DragEffects = DragDropEffects.Move;
        }
    }

    private void OnFileListDrop(object? sender, DragEventArgs e)
    {
        var dragged = e.DataTransfer.TryGetValue(FileDragFormat);
        if (dragged is null)
        {
            return;
        }

        var targetItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
        var targetIndex = targetItem is not null
            ? FileListBox.IndexFromContainer(targetItem)
            : ViewModel.FileList.Files.Count - 1;

        ViewModel.FileList.MoveTo(dragged, targetIndex);
    }

    private void OnRemoveSelectedFileClick(object? sender, RoutedEventArgs e)
    {
        foreach (var item in FileListBox.SelectedItems!.Cast<PdfFileEntryViewModel>().ToList())
        {
            ViewModel.FileList.Remove(item);
        }
    }

    private void OnMoveFileUpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.FileList.MoveSelectionUp(FileListBox.SelectedItems!.Cast<PdfFileEntryViewModel>().ToList());
    }

    private void OnMoveFileDownClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.FileList.MoveSelectionDown(FileListBox.SelectedItems!.Cast<PdfFileEntryViewModel>().ToList());
    }

    // ---- しおりツリー: D&Dによる並べ替え・再親子付け ----

    private void OnBookmarkTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 横スクロールバー自体の操作(ドラッグ等)まで巻き戻してしまわないよう、スクロールバー上の
        // クリックは対象外にする。
        if ((e.Source as Visual)?.FindAncestorOfType<ScrollBar>(true) is null)
        {
            PreserveBookmarkTreeHorizontalScrollPosition();
        }

        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
        _bookmarkPressedNode = item?.DataContext as BookmarkNodeViewModel;
        _bookmarkPressedArgs = _bookmarkPressedNode is not null ? e : null;

        if (item is null)
        {
            // 行の実要素(タイトル欄等)が無い部分(レベル表示の左側の余白、結合後ページ表示の
            // 右側の余白)をクリックした場合でも選択可能にする(強調表示の範囲自体は変更しない)。
            SelectBookmarkRowAtY(e.GetPosition(BookmarkTreeView).Y);
        }
    }

    /// <summary>
    /// BookmarkTreeView内の指定Y座標(TreeView基準)に表示されている行を探し、選択状態にする。
    /// 展開中の子ノードも再帰的に対象とする。
    /// </summary>
    private void SelectBookmarkRowAtY(double y)
    {
        if (FindTreeViewItemAtY(BookmarkTreeView, y) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private TreeViewItem? FindTreeViewItemAtY(ItemsControl container, double y)
    {
        for (var i = 0; i < container.ItemCount; i++)
        {
            if (container.ContainerFromIndex(i) is not TreeViewItem item)
            {
                continue;
            }

            if (item.TranslatePoint(new Point(0, 0), BookmarkTreeView) is not { } topLeft)
            {
                continue;
            }

            var headerHeight = FindOwnHeaderPanel(item)?.Bounds.Height ?? item.Bounds.Height;
            if (y >= topLeft.Y && y < topLeft.Y + headerHeight)
            {
                return item;
            }

            if (item.IsExpanded && FindTreeViewItemAtY(item, y) is { } childHit)
            {
                return childHit;
            }
        }

        return null;
    }

    /// <summary>TreeViewItem自身のヘッダー行(TreeDataTemplateのルートStackPanel)を探す。
    /// 子として描画されたネストTreeViewItemの内部には descend しない。</summary>
    private static StackPanel? FindOwnHeaderPanel(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is StackPanel { Tag: BookmarkNodeViewModel } panel)
            {
                return panel;
            }

            if (child is TreeViewItem)
            {
                continue;
            }

            if (FindOwnHeaderPanel(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private async void OnBookmarkTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_bookmarkDragInProgress || _bookmarkPressedNode is null || _bookmarkPressedArgs is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(BookmarkTreeView).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var dragged = _bookmarkPressedNode;
        var pressArgs = _bookmarkPressedArgs;
        _bookmarkDragInProgress = true;
        _bookmarkPressedNode = null;
        _bookmarkPressedArgs = null;

        try
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(BookmarkDragFormat, dragged));
            await DragDrop.DoDragDropAsync(pressArgs, dataTransfer, DragDropEffects.Move);
        }
        finally
        {
            _bookmarkDragInProgress = false;
        }
    }

    private void OnBookmarkTreeDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BookmarkDragFormat))
        {
            e.DragEffects = DragDropEffects.Move;
        }

        var plan = ResolveBookmarkDropPlan(e);
        if (plan is { } p)
        {
            ShowBookmarkDropIndicator(p.LineX, p.LineY);
        }
        else
        {
            RemoveBookmarkDropIndicator();
        }

        UpdateBookmarkAutoScroll(e);
    }

    private void OnBookmarkTreeDragLeave(object? sender, RoutedEventArgs e)
    {
        RemoveBookmarkDropIndicator();
        StopBookmarkAutoScroll();
    }

    /// <summary>
    /// ドラッグ中のカーソル位置から、ドロップ対象ノードと挿入方法(子/兄弟)、
    /// および挿入位置インジケータの描画座標を求める。対象行の外側(空白領域)の場合はnull。
    /// e.Sourceによるヒットテストだと、行内の実要素(タイトル欄等)が無い部分(レベル表示の左側の余白、
    /// 結合後ページ表示の右側の余白)にカーソルがある場合にヒットする要素が無く、ドロップ対象が
    /// 見つからなくなる。SelectBookmarkRowAtYと同様、カーソルのY座標から幾何的に行を探すことで、
    /// 行の全幅でドロップを受け付けるようにする。
    /// </summary>
    private BookmarkDropPlan? ResolveBookmarkDropPlan(DragEventArgs e)
    {
        var pointInTree = e.GetPosition(BookmarkTreeView);
        var targetItem = FindTreeViewItemAtY(BookmarkTreeView, pointInTree.Y);
        if (targetItem?.DataContext is not BookmarkNodeViewModel targetNode)
        {
            return null;
        }

        var headerPanel = FindOwnHeaderPanel(targetItem);
        if (headerPanel is null || headerPanel.Bounds.Height <= 0)
        {
            return null;
        }

        var headerTopLeft = headerPanel.TranslatePoint(new Point(0, 0), BookmarkTreeView) ?? default;
        var mouseWithinHeader = e.GetPosition(headerPanel);
        var insertAsChild = mouseWithinHeader.Y < headerPanel.Bounds.Height / 2;

        double lineX;
        double lineY;
        if (insertAsChild)
        {
            // 子として挿入: そのノードの一段深い位置、ヘッダー直下(=先頭の子の位置)に線を引く。
            lineX = headerTopLeft.X + BookmarkChildIndent;
            lineY = headerTopLeft.Y + headerPanel.Bounds.Height;
        }
        else
        {
            // 兄弟として挿入: そのノードと同じ深さで、展開中の子要素を含めた行全体の下端に線を引く。
            var itemTopLeft = targetItem.TranslatePoint(new Point(0, 0), BookmarkTreeView) ?? default;
            lineX = headerTopLeft.X;
            lineY = itemTopLeft.Y + targetItem.Bounds.Height;
        }

        return new BookmarkDropPlan(targetNode, insertAsChild, lineX, lineY);
    }

    private void ShowBookmarkDropIndicator(double x, double y)
    {
        var width = Math.Max(0, BookmarkTreeView.Bounds.Width - x);

        BookmarkDropIndicatorLine.StartPoint = new Point(x, y);
        BookmarkDropIndicatorLine.EndPoint = new Point(x + width, y);
        BookmarkDropIndicatorLine.IsVisible = true;

        Canvas.SetLeft(BookmarkDropIndicatorDot, x - BookmarkDropIndicatorDot.Width / 2);
        Canvas.SetTop(BookmarkDropIndicatorDot, y - BookmarkDropIndicatorDot.Height / 2);
        BookmarkDropIndicatorDot.IsVisible = true;
    }

    private void RemoveBookmarkDropIndicator()
    {
        BookmarkDropIndicatorLine.IsVisible = false;
        BookmarkDropIndicatorDot.IsVisible = false;
    }

    /// <summary>
    /// ドラッグ中のカーソルがツリー表示範囲の上端/下端付近(AutoScrollEdgeMargin以内)にある間、
    /// タイマーで少しずつスクロールし続ける。ツリー描画範囲外へドラッグを続けられるようにするための対応。
    /// </summary>
    private void UpdateBookmarkAutoScroll(DragEventArgs e)
    {
        _bookmarkTreeScrollViewer ??= BookmarkTreeView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_bookmarkTreeScrollViewer is null)
        {
            return;
        }

        var position = e.GetPosition(BookmarkTreeView);
        var height = BookmarkTreeView.Bounds.Height;

        if (position.Y < 0 || position.Y > height)
        {
            StopBookmarkAutoScroll();
        }
        else if (position.Y < AutoScrollEdgeMargin)
        {
            StartBookmarkAutoScroll(-AutoScrollStep);
        }
        else if (position.Y > height - AutoScrollEdgeMargin)
        {
            StartBookmarkAutoScroll(AutoScrollStep);
        }
        else
        {
            StopBookmarkAutoScroll();
        }
    }

    private void StartBookmarkAutoScroll(double step)
    {
        _bookmarkAutoScrollStep = step;
        if (!_bookmarkAutoScrollTimer.IsEnabled)
        {
            _bookmarkAutoScrollTimer.Start();
        }
    }

    private void StopBookmarkAutoScroll() => _bookmarkAutoScrollTimer.Stop();

    private void OnBookmarkTreeDrop(object? sender, DragEventArgs e)
    {
        RemoveBookmarkDropIndicator();
        StopBookmarkAutoScroll();

        var dragged = e.DataTransfer.TryGetValue(BookmarkDragFormat);
        if (dragged is null)
        {
            return;
        }

        if (ResolveBookmarkDropPlan(e) is { } plan)
        {
            if (plan.InsertAsChild)
            {
                // 行の上半分にドロップ: そのノードの子(先頭)としてぶら下げる。
                ViewModel.BookmarkTree.Move(dragged, plan.TargetNode, 0);
            }
            else
            {
                // 行の下半分にドロップ: そのノードと並列(直後の兄弟)として挿入する。
                var newParent = plan.TargetNode.Parent;
                var siblings = newParent?.Children ?? ViewModel.BookmarkTree.RootNodes;
                var newIndex = siblings.IndexOf(plan.TargetNode) + 1;
                ViewModel.BookmarkTree.Move(dragged, newParent, newIndex);
            }
        }
        else
        {
            // 何もない場所へのドロップはルート末尾へ移動する。
            ViewModel.BookmarkTree.Move(dragged, null, ViewModel.BookmarkTree.RootNodes.Count);
        }

        RecomputeTitleColumnWidth();
    }

    private void OnBookmarkTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LevelCapButton.IsEnabled = BookmarkTreeView.SelectedItem is BookmarkNodeViewModel { Children.Count: > 0 };
        UpdateLevelButtonsEnabled();
    }

    /// <summary>
    /// しおり行1件分は多数のコントロールを横に並べた幅広の行のため、ウィンドウ幅より広い場合は
    /// 横スクロールバーが表示される(HorizontalScrollBarVisibility="Auto")。この状態で行をクリックすると、
    /// 既定の動作(選択・フォーカス変更時にScrollViewerが対象を画面内へ収めようとする)により
    /// 行全体(横方向含む)を表示しようとして、意図せず横スクロール位置が動いてしまう不具合があった。
    /// クリック直後(選択処理が始まる前)の横スクロール位置を保存しておき、選択・フォーカス変更に伴う
    /// 一連の処理が完了した後のタイミング(Dispatcher.UIThread.PostでDispatcherPriority.ContextIdleまで
    /// キューを空にしてから)に元の位置へ復元することで、原因となる個々の処理(既定の自動スクロールか、
    /// SelectBookmarkRowAtYのitem.Focus()か等)を問わず、確実に横スクロール位置を保つ。縦方向は
    /// 復元しないため、キーボード操作で画面外の行を選択した場合の縦方向の自動スクロールはこれまでどおり機能する。
    /// </summary>
    private void PreserveBookmarkTreeHorizontalScrollPosition()
    {
        _bookmarkTreeScrollViewer ??= BookmarkTreeView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_bookmarkTreeScrollViewer is null)
        {
            return;
        }

        var horizontalOffset = _bookmarkTreeScrollViewer.Offset.X;
        Dispatcher.UIThread.Post(() =>
        {
            if (_bookmarkTreeScrollViewer is { } scrollViewer)
            {
                scrollViewer.Offset = new Vector(horizontalOffset, scrollViewer.Offset.Y);
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void UpdateLevelButtonsEnabled()
    {
        var node = BookmarkTreeView.SelectedItem as BookmarkNodeViewModel;
        PromoteLevelButton.IsEnabled = node is not null && ViewModel.BookmarkTree.CanPromoteLevel(node);
        DemoteLevelButton.IsEnabled = node is not null && ViewModel.BookmarkTree.CanDemoteLevel(node);
    }

    private void OnPromoteLevelClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.PromoteLevel(node);
            RecomputeTitleColumnWidth();
            UpdateLevelButtonsEnabled();
        }
    }

    private void OnDemoteLevelClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.DemoteLevel(node);
            RecomputeTitleColumnWidth();
            UpdateLevelButtonsEnabled();
        }
    }

    private async void OnSetLevelCapClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            await ViewModel.BookmarkTree.SetChildLevelCapAsync(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnAddRootBookmarkClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.BookmarkTree.AddRoot();
        RecomputeTitleColumnWidth();
    }

    private void OnAddChildBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.AddChild(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnAddSiblingBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.AddSiblingAfter(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnDeleteBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (BookmarkTreeView.SelectedItem is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.Remove(node);
            RecomputeTitleColumnWidth();
        }
    }

    /// <summary>
    /// 結合前ページ数テキストボックスのコンテキストメニュー(リセット)。ContextMenuはプロパティ経由で
    /// TextBoxへ割り当てているため、MenuItem.DataContextはそのTextBoxのDataContext
    /// (=対象のBookmarkNodeViewModel)を継承する。対象ノードが属するPDFファイルに関係する
    /// 結合前ページ数の編集を、ファイル単位で一括リセットする(そのノード単体だけでなく、
    /// 同一ファイル内の他のノードへの編集もすべて元へ戻す)。
    /// </summary>
    private void OnResetPreOffsetPageNumberClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.ResetFilePageNumbers(node);
        }
    }

    /// <summary>
    /// ツリー開閉レベルテキストボックスがフォーカスを失った際に、入力値が数値以外またはツリーに
    /// 含まれない数値であれば空欄へ正規化する。適用自体はBookmarkTreeViewModel.ExpandLevelInputの
    /// 値変更購読が(Avaloniaでは入力のたびに)随時行う。
    /// </summary>
    private void OnExpandLevelTextBoxLostFocus(object? sender, RoutedEventArgs e) =>
        ViewModel.BookmarkTree.NormalizeExpandLevelInput();

    /// <summary>
    /// リンク編集画面のしおり一覧をクリックした時の動作。ジャンプ先の指定待ち(PendingSelection)の間は
    /// クリックしたしおりをジャンプ先として選択し、それ以外の場合は該当ページへプレビューをジャンプする。
    /// </summary>
    private void OnLinkEditorBookmarkClick(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { Tag: BookmarkNode bookmark })
        {
            return;
        }

        var linkEditor = ViewModel.LinkEditor;
        if (linkEditor.PendingSelection.Value is not null)
        {
            linkEditor.CreateLinkToBookmarkCommand.Execute(bookmark);
        }
        else
        {
            linkEditor.JumpToPageCommand.Execute(bookmark.OriginalPageIndex);
        }
    }

    private bool _isSelectingLinkText;
    private Point _linkSelectionStartPoint;

    /// <summary>連続スクロールプレビューの1ページ分のコンテナがビューポートに入った時
    /// (VirtualizingStackPanelによるコンテナの初回生成・再利用のいずれでも発生する)に、そのページの
    /// 画像描画をトリガーする。現在ページのコンテナであれば、生成タイミングによってはRedrawLinkOverlayが
    /// 実体化前に呼ばれて何もできなかった可能性があるため、ここでも改めてオーバーレイを描画する。</summary>
    private void OnPageSlotLoaded(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PdfPageSlotViewModel slot)
        {
            return;
        }

        _ = ViewModel.LinkEditor.LoadPageSlotAsync(slot.PageIndex);

        if (slot.PageIndex == ViewModel.LinkEditor.CurrentPageIndex.Value)
        {
            RedrawLinkOverlay();
        }
    }

    /// <summary>コンテナがビューポートから外れた(リサイクルされた)時に、保持していた画像を破棄する
    /// (数千ページ規模のPDFでも全ページ分のビットマップを同時に保持しないため)。</summary>
    private void OnPageSlotUnloaded(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PdfPageSlotViewModel slot)
        {
            ViewModel.LinkEditor.UnloadPageSlot(slot.PageIndex);
        }
    }

    /// <summary>連続スクロールプレビューの各ページコンテナに設定しているMargin(下方向の余白)。
    /// オフセット計算をXAML側のListBox.Stylesと一致させるために使う。</summary>
    private const double PageItemBottomMargin = 4;

    /// <summary>プレビューのスクロール位置から、現在の操作対象とみなすページ(ビューポート内で
    /// 最も表示面積が大きいページ)を求め、CurrentPageIndexへ反映する。これにより、ページ送り
    /// ボタンを使わずスクロールするだけで1ページ目から最終ページまで移動できる。</summary>
    private void OnPdfPreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = PdfPageListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        if (ComputeMostVisiblePageIndex(scrollViewer.Offset.Y, scrollViewer.Viewport.Height) is not { } pageIndex || pageIndex == ViewModel.LinkEditor.CurrentPageIndex.Value)
        {
            return;
        }

        _isSyncingCurrentPageFromScroll = true;
        try
        {
            ViewModel.LinkEditor.CurrentPageIndex.Value = pageIndex;
        }
        finally
        {
            _isSyncingCurrentPageFromScroll = false;
        }
    }

    /// <summary>垂直スクロールオフセット・ビューポート高さ(いずれもpx)から、ビューポート内で
    /// 最も表示面積が大きいページ番号を算出する。全ページ同一のプレースホルダ高さを前提とする
    /// (ヒットテストによる検出は、仮想化パネルの内部構造に依存し不安定なため採用しない)。</summary>
    private int? ComputeMostVisiblePageIndex(double verticalOffset, double viewportHeight)
    {
        var linkEditor = ViewModel.LinkEditor;
        var itemHeight = linkEditor.PlaceholderHeight.Value + PageItemBottomMargin;
        if (itemHeight <= 0 || linkEditor.PageSlots.Count == 0)
        {
            return null;
        }

        var viewportTop = verticalOffset;
        var viewportBottom = verticalOffset + viewportHeight;
        var lastIndex = linkEditor.PageSlots.Count - 1;
        var firstCandidate = Math.Clamp((int)(viewportTop / itemHeight) - 1, 0, lastIndex);
        var lastCandidate = Math.Clamp((int)(viewportBottom / itemHeight) + 1, 0, lastIndex);

        var bestIndex = firstCandidate;
        var bestVisibleHeight = -1.0;
        for (var i = firstCandidate; i <= lastCandidate; i++)
        {
            var itemTop = i * itemHeight;
            var visibleHeight = Math.Min(viewportBottom, itemTop + itemHeight) - Math.Max(viewportTop, itemTop);
            if (visibleHeight > bestVisibleHeight)
            {
                bestVisibleHeight = visibleHeight;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>指定ページの先頭が正確にビューポート上端へ来る位置までスクロールする
    /// (ページ送りボタン・しおりジャンプ用)。
    /// ListBox.ScrollIntoViewは「最小限のスクロールで対象を見えるようにする」動作のため、
    /// 対象ページの末尾がビューポート下端に揃ってしまい先頭に揃わないことがある。さらに
    /// ScrollIntoViewと直後の直接オフセット指定を続けて呼ぶと、どちらか一方の指示しか
    /// 反映されないことがあったため、ScrollIntoViewは使わず直接オフセットを指定するだけにする
    /// (対象が仮想化によりまだ実体化されていなくても、VirtualizingStackPanelはスクロール位置に
    /// 応じてレイアウト時にコンテナを生成するため、これだけで数千ページ先へのジャンプにも対応できる)。</summary>
    private void ScrollToPage(int pageIndex)
    {
        var linkEditor = ViewModel.LinkEditor;
        if (pageIndex < 0 || pageIndex >= linkEditor.PageSlots.Count)
        {
            return;
        }

        var scrollViewer = PdfPageListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scrollViewer is not null)
        {
            var offset = pageIndex * (linkEditor.PlaceholderHeight.Value + PageItemBottomMargin);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, offset);
        }

        RedrawLinkOverlay();
    }

    private void OnPdfPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var hitLayer = (Control)sender!;
        var linkEditor = ViewModel.LinkEditor;
        var position = e.GetPosition(hitLayer);
        var (pdfX, pdfY) = PdfCoordinateMapper.ToPdf(position.X, position.Y, linkEditor.PageHeight.Value, linkEditor.ZoomScale.Value);

        if (linkEditor.IsPickingArbitraryTarget.Value)
        {
            linkEditor.PickArbitraryTargetAndCreateLink(linkEditor.CurrentPageIndex.Value, pdfX, pdfY);
            RedrawLinkOverlay();
            return;
        }

        _isSelectingLinkText = true;
        _linkSelectionStartPoint = position;
        e.Pointer.Capture(hitLayer);
        linkEditor.BeginTextSelection(pdfX, pdfY);
        DrawLiveSelectionRect(hitLayer, position, position);
    }

    private void OnPdfPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSelectingLinkText)
        {
            return;
        }

        var hitLayer = (Control)sender!;
        var linkEditor = ViewModel.LinkEditor;
        var position = e.GetPosition(hitLayer);
        var (pdfX, pdfY) = PdfCoordinateMapper.ToPdf(position.X, position.Y, linkEditor.PageHeight.Value, linkEditor.ZoomScale.Value);
        linkEditor.UpdateTextSelection(pdfX, pdfY);
        DrawLiveSelectionRect(hitLayer, _linkSelectionStartPoint, position);
    }

    private void OnPdfPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSelectingLinkText)
        {
            return;
        }

        _isSelectingLinkText = false;
        e.Pointer.Capture(null);
        ViewModel.LinkEditor.EndTextSelection();
        RedrawLinkOverlay();
    }

    /// <summary>選択・オーバーレイのヒットレイヤー(Rectangle)と同じGrid内にある、兄弟要素の
    /// LinkOverlayCanvasを探す(連続スクロール表示では現在ページのコンテナ以外にも同名の
    /// Canvasが存在しうるため、常に「今操作しているコンテナ自身」のCanvasを使う必要がある)。</summary>
    private static Canvas? FindSiblingOverlayCanvas(Control hitLayer) =>
        hitLayer.GetVisualParent()?.GetVisualDescendants().OfType<Canvas>().FirstOrDefault();

    /// <summary>ドラッグ中の選択範囲を、簡易的な単一矩形(始点〜現在点の外接矩形)として描画する。</summary>
    private void DrawLiveSelectionRect(Control hitLayer, Point start, Point current)
    {
        if (FindSiblingOverlayCanvas(hitLayer) is not { } canvas)
        {
            return;
        }

        canvas.Children.Clear();
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var rect = new Rectangle
        {
            Width = Math.Abs(current.X - start.X),
            Height = Math.Abs(current.Y - start.Y),
            Fill = new SolidColorBrush(Color.FromArgb(80, 30, 144, 255)),
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }

    /// <summary>現在ページのコンテナが実体化されている場合に限り、そのコンテナ内のLinkOverlayCanvasを返す。
    /// 連続スクロール表示では、現在ページがスクロール未到達等でまだ仮想化により実体化されていないことがあり、
    /// その間は描画対象が存在しないためnullを返す(コンテナが実体化された時にOnPageSlotLoadedから
    /// 改めて呼ばれる)。</summary>
    private Canvas? FindCurrentPageOverlayCanvas()
    {
        var container = PdfPageListBox.ContainerFromIndex(ViewModel.LinkEditor.CurrentPageIndex.Value);
        return container?.GetVisualDescendants().OfType<Canvas>().FirstOrDefault();
    }

    /// <summary>現在ページに属する確定済みリンクのホットスポットを、半透明の矩形として描画し直す。</summary>
    private void RedrawLinkOverlay()
    {
        if (FindCurrentPageOverlayCanvas() is not { } canvas)
        {
            return;
        }

        canvas.Children.Clear();

        var linkEditor = ViewModel.LinkEditor;
        var pageHeight = linkEditor.PageHeight.Value;
        var scale = linkEditor.ZoomScale.Value;
        var currentPage = linkEditor.CurrentPageIndex.Value;

        foreach (var link in linkEditor.Links)
        {
            if (link.SourcePageIndex != currentPage)
            {
                continue;
            }

            var pixelRect = PdfCoordinateMapper.ToPixelRect(link.SourceRect, pageHeight, scale);
            var rect = new Rectangle
            {
                Width = pixelRect.Right - pixelRect.Left,
                Height = pixelRect.Bottom - pixelRect.Top,
                Fill = new SolidColorBrush(Color.FromArgb(60, 0, 200, 0)),
                Stroke = Brushes.Green,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(rect, pixelRect.Left);
            Canvas.SetTop(rect, pixelRect.Top);
            canvas.Children.Add(rect);
        }
    }

    /// <summary>リンク一覧の「表示」ボタン: そのリンクのホットスポットがあるページへプレビューをジャンプする(動作確認用)。</summary>
    private void OnLinkGroupJumpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LinkGroupInfo group })
        {
            ViewModel.LinkEditor.JumpToPageCommand.Execute(group.SourcePageIndex);
        }
    }

    /// <summary>リンク一覧の「編集」ボタン: 既存リンクを一旦外し、同じホットスポットのままジャンプ先を選び直せる状態にする。</summary>
    private void OnLinkGroupEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LinkGroupInfo group })
        {
            ViewModel.LinkEditor.EditLinkGroupCommand.Execute(group.GroupId);
        }
    }

    private void OnLinkGroupDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LinkGroupInfo group })
        {
            ViewModel.LinkEditor.DeleteLinkGroupCommand.Execute(group.GroupId);
        }
    }
}
