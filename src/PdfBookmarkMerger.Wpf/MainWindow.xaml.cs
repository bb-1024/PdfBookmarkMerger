using System.Collections.Specialized;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.WpfApp.Controls;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.WpfApp;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string FileDragFormat = "PdfBookmarkMerger.PdfFileEntryViewModel";
    private const string BookmarkDragFormat = "PdfBookmarkMerger.BookmarkNodeViewModel";

    /// <summary>しおり行のドロップ判定で、上半分/下半分どちらにカーソルがあるかを示す境界の目安インデント量(px)。</summary>
    private const double BookmarkChildIndent = 19;

    private Point _fileDragStart;
    private Point _bookmarkDragStart;

    /// <summary>D&D中、この余白(px)以内にカーソルが入るとツリーの自動スクロールを開始する。</summary>
    private const double AutoScrollEdgeMargin = 32;

    /// <summary>自動スクロールの1ティックあたりの移動量(px)。</summary>
    private const double AutoScrollStep = 18;

    private AdornerLayer? _bookmarkAdornerLayer;
    private BookmarkInsertionAdorner? _bookmarkDropAdorner;
    private PlaceholderTextAdorner? _fileListPlaceholderAdorner;
    private ScrollViewer? _bookmarkTreeScrollViewer;
    private DispatcherTimer? _bookmarkAutoScrollTimer;
    private double _bookmarkAutoScrollStep;

    /// <summary>IsBusyが5秒以上継続した場合にのみ、詳細進捗(BusyDetailText)を表示するためのタイマー。</summary>
    private readonly DispatcherTimer _busyDetailTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private readonly record struct BookmarkDropPlan(BookmarkNodeViewModel TargetNode, bool InsertAsChild, double LineX, double LineY);

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

        _busyDetailTimer.Tick += OnBusyDetailTimerTick;

        Loaded += OnMainWindowLoaded;
        Closed += OnMainWindowClosed;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        // ファイル一覧が空の間、AdornerLayer経由でヒントテキストを重ねる。
        // ListBoxの可視ツリー自体は一切変更しないため、D&Dのヒットテストに影響しない。
        ViewModel.FileList.HasFiles.Subscribe(_ => UpdateFileListPlaceholder()).AddTo(_viewModelSubscriptions);

        // しおり編集画面に入るたびに、タイトル列の幅を現在のタイトル群に合わせて再計算する。
        ViewModel.Step.Subscribe(step =>
        {
            if (step == WorkflowStep.EditBookmarks)
            {
                RecomputeTitleColumnWidth();
            }
        }).AddTo(_viewModelSubscriptions);

        ViewModel.IsBusy.Subscribe(OnIsBusyChanged).AddTo(_viewModelSubscriptions);

        ViewModel.FileList.Files.CollectionChanged += OnFileListFilesCollectionChanged;
        UpdateFileMoveButtonsEnabled();

        // Undo(元に戻す)はRootNodes全体を作り直すため、タイトル列幅もあわせて再計算する。
        // VM側のUndoCommand.Subscribe(Undo)(RootNodes再構築)が先に完了してから呼ばれる。
        ViewModel.BookmarkTree.UndoCommand.Subscribe(RecomputeTitleColumnWidth).AddTo(_viewModelSubscriptions);

        // リンクのホットスポット表示は、対象ページ・拡大率・ページ高さ(座標変換の基準)のいずれかが
        // 変わるたびに再計算が必要。リンク自体の追加・削除でも当然再描画する。
        // PendingSelectionは、リンク確定(Links変更で別途再描画される)以外にキャンセル
        // (CancelPendingSelectionCommand、Linksを一切変更しない)でも変化するため、これも
        // 個別に購読しないと、選択中・確定待ちの範囲を示す青い矩形がキャンセル後も残ってしまう。
        ViewModel.LinkEditor.Links.CollectionChanged += OnLinkEditorLinksCollectionChanged;
        ViewModel.LinkEditor.ZoomScale.Subscribe(_ => RedrawLinkOverlay()).AddTo(_viewModelSubscriptions);
        ViewModel.LinkEditor.PageHeight.Subscribe(_ => RedrawLinkOverlay()).AddTo(_viewModelSubscriptions);
        ViewModel.LinkEditor.PendingSelection.Subscribe(_ => RedrawLinkOverlay()).AddTo(_viewModelSubscriptions);

        // 一度リンク編集・保存を行った後、ファイル選択からやり直して再度リンク編集画面へ戻ってきた際、
        // 直前のセッションのスクロール可能範囲(Extent)が残ったままになる不具合の対策。
        // PageSlots.Clear()+Add(...)でItemsSourceの中身を丸ごと差し替えても、
        // VirtualizingStackPanel(ScrollUnit="Pixel")は内部的に保持している「項目が均一サイズか」の
        // 判定・それに基づく推定Extentのキャッシュを、単純なInvalidateMeasure/UpdateLayoutだけでは
        // 再計算しないことがある(WPFの既知の挙動 — dotnet/wpf#7017等でも
        // VirtualizingStackPanel.SyncUniformSizeFlags周りの内部状態管理に起因する問題が報告されている)。
        // IsVirtualizingを一度falseにしてtrueへ戻すと、パネルが仮想化モードそのものを切り替えるため
        // 内部状態が完全に破棄され、再度trueにした時点でExtentを含め作り直される。
        ViewModel.LinkEditor.LoadGeneration.Subscribe(_ =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                VirtualizingPanel.SetIsVirtualizing(PdfPageListBox, false);
                PdfPageListBox.UpdateLayout();
                VirtualizingPanel.SetIsVirtualizing(PdfPageListBox, true);
                PdfPageListBox.UpdateLayout();

                if (FindDescendant<ScrollViewer>(PdfPageListBox) is { } scrollViewer)
                {
                    scrollViewer.ScrollToVerticalOffset(0);
                }

                PdfPageListBox.UpdateLayout();
                RedrawLinkOverlay();
            }));
        }).AddTo(_viewModelSubscriptions);

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

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollToPage(pageIndex)));
        }).AddTo(_viewModelSubscriptions);
    }

    private void OnFileListFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateFileMoveButtonsEnabled();

    /// <summary>
    /// 言語切り替え時、このウィンドウはViewModelより先に破棄される(ReplaceMainWindowForLanguageChangeが
    /// 新ウィンドウへ同じViewModelを引き継ぐため)。ここで購読を解除しないと、古いウィンドウの
    /// コールバックがViewModelの変化のたびに(既に閉じた)自分自身のUI要素を触り続けてしまう。
    /// </summary>
    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        Closed -= OnMainWindowClosed;
        ViewModel.FileList.Files.CollectionChanged -= OnFileListFilesCollectionChanged;
        ViewModel.LinkEditor.Links.CollectionChanged -= OnLinkEditorLinksCollectionChanged;
        _viewModelSubscriptions.Dispose();
    }

    private void OnLinkEditorLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawLinkOverlay();

    private void OnFileListSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFileMoveButtonsEnabled();

    /// <summary>
    /// 選択中のファイルがリストの最上部/最下部にあり、それ以上その方向へ移動できない場合に、
    /// 対応する「上へ」「下へ」ボタンを非活性化する。何も選択されていない場合は両方とも非活性化する。
    /// </summary>
    private void UpdateFileMoveButtonsEnabled()
    {
        var selected = FileListBox.SelectedItems.Cast<PdfFileEntryViewModel>().ToList();
        var (canMoveUp, canMoveDown) = ViewModel.FileList.GetMoveAvailability(selected);
        MoveFileUpButton.IsEnabled = canMoveUp;
        MoveFileDownButton.IsEnabled = canMoveDown;
    }

    private void OnIsBusyChanged(bool isBusy)
    {
        BusyDetailText.Visibility = Visibility.Collapsed;
        _busyDetailTimer.Stop();
        if (isBusy)
        {
            _busyDetailTimer.Start();
        }
    }

    private void OnBusyDetailTimerTick(object? sender, EventArgs e)
    {
        _busyDetailTimer.Stop();
        BusyDetailText.Visibility = Visibility.Visible;
    }

    // ---- しおりツリー: タイトル列の幅をタイトル文字列の実測幅に追従させる ----

    /// <summary>タイトルTextBox共通のTextChangedハンドラ。編集中の行に関わらずツリー全体の幅を再計算する。</summary>
    private void OnBookmarkTitleTextChanged(object sender, TextChangedEventArgs e) => RecomputeTitleColumnWidth();

    /// <summary>
    /// 現在のしおりツリー全ノードのタイトルを実測し、最も幅を必要とするノードに合わせて
    /// タイトル列の共有基準幅(BookmarkTree.TitleColumnBaseWidth)を更新する。
    /// 各行の実際の幅は、この基準幅から階層の深さ分を差し引く形でDepthToTitleWidthConverterが求めるため、
    /// 更新後も列の縦位置は揃ったまま保たれる。
    /// </summary>
    private void RecomputeTitleColumnWidth()
    {
        const double indentPerLevel = 19;
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

    private double MeasureTextWidth(string text)
    {
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formattedText.Width;
    }

    private void UpdateFileListPlaceholder()
    {
        var layer = AdornerLayer.GetAdornerLayer(FileListBox);
        if (layer is null)
        {
            return;
        }

        if (ViewModel.FileList.HasFiles.Value)
        {
            if (_fileListPlaceholderAdorner is not null)
            {
                layer.Remove(_fileListPlaceholderAdorner);
                _fileListPlaceholderAdorner = null;
            }
        }
        else if (_fileListPlaceholderAdorner is null)
        {
            var foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
            _fileListPlaceholderAdorner = new PlaceholderTextAdorner(
                FileListBox, Strings.DragDropPlaceholder, foreground);
            layer.Add(_fileListPlaceholderAdorner);
        }
    }

    public MainWindowViewModel ViewModel { get; }

    // ---- ウィンドウ全体へのD&D (エクスプローラーからのファイル/フォルダ) ----

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.Step.Value == WorkflowStep.SelectFiles && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.Step.Value != WorkflowStep.SelectFiles || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        ViewModel.FileList.AddPaths(paths);
        e.Handled = true;
    }

    // ---- ファイル一覧: 内部D&Dによる並べ替え ----

    private void OnFileListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fileDragStart = e.GetPosition(null);
    }

    private void OnFileListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not PdfFileEntryViewModel entry)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(FileDragFormat, entry);
        DragDrop.DoDragDrop(FileListBox, data, DragDropEffects.Move);
    }

    private void OnFileListDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FileDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnFileListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(FileDragFormat) is not PdfFileEntryViewModel dragged)
        {
            return;
        }

        var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        var targetIndex = targetItem is not null
            ? FileListBox.ItemContainerGenerator.IndexFromContainer(targetItem)
            : ViewModel.FileList.Files.Count - 1;

        ViewModel.FileList.MoveTo(dragged, targetIndex);
        e.Handled = true;
    }

    private void OnRemoveSelectedFileClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in FileListBox.SelectedItems.Cast<PdfFileEntryViewModel>().ToList())
        {
            ViewModel.FileList.Remove(item);
        }
    }

    private void OnMoveFileUpClick(object sender, RoutedEventArgs e)
    {
        ViewModel.FileList.MoveSelectionUp(FileListBox.SelectedItems.Cast<PdfFileEntryViewModel>().ToList());
    }

    private void OnMoveFileDownClick(object sender, RoutedEventArgs e)
    {
        ViewModel.FileList.MoveSelectionDown(FileListBox.SelectedItems.Cast<PdfFileEntryViewModel>().ToList());
    }

    // ---- しおりツリー: D&Dによる並べ替え・再親子付け ----

    private void OnBookmarkTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _bookmarkDragStart = e.GetPosition(null);

        // 横スクロールバー自体の操作(ドラッグ等)まで巻き戻してしまわないよう、スクロールバー上の
        // クリックは対象外にする。
        if (FindAncestor<ScrollBar>((DependencyObject)e.OriginalSource) is null)
        {
            PreserveBookmarkTreeHorizontalScrollPosition();
        }

        // 行の実要素(タイトル欄・ComboBox等)が無い部分(レベル表示の左側のインデント余白、
        // 結合後ページ表示の右側の余白)をクリックした場合、既定ではその行にヒットテストされる
        // 要素が無く選択が行われない。ヒットしたTreeViewItemが見つからない場合は、
        // クリックしたY座標が属する行を幾何的に探して選択状態にする(強調表示の範囲自体は変更しない)。
        if (FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource) is null)
        {
            SelectBookmarkRowAtY(e.GetPosition(BookmarkTreeView).Y);
        }
    }

    /// <summary>
    /// BookmarkTreeView内の指定Y座標(TreeView基準)に表示されている行を探し、選択状態にする。
    /// 展開中の子ノードも再帰的に対象とする。
    /// </summary>
    private void SelectBookmarkRowAtY(double y)
    {
        var item = FindTreeViewItemAtY(BookmarkTreeView, y);
        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private TreeViewItem? FindTreeViewItemAtY(ItemsControl container, double y)
    {
        for (var i = 0; i < container.Items.Count; i++)
        {
            if (container.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item || item.ActualHeight <= 0)
            {
                continue;
            }

            var topLeft = item.TransformToAncestor(BookmarkTreeView).Transform(new Point(0, 0));
            var headerHeight = FindOwnHeaderBorder(item)?.ActualHeight ?? item.ActualHeight;
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

    private void OnBookmarkTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _bookmarkDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _bookmarkDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not BookmarkNodeViewModel node)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(BookmarkDragFormat, node);
        DragDrop.DoDragDrop(BookmarkTreeView, data, DragDropEffects.Move);
        RemoveBookmarkDropIndicator();
    }

    private void OnBookmarkTreeDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(BookmarkDragFormat))
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (ResolveBookmarkDropPlan(e) is { } plan)
        {
            ShowBookmarkDropIndicator(plan.LineX, plan.LineY);
        }
        else
        {
            RemoveBookmarkDropIndicator();
        }

        UpdateBookmarkAutoScroll(e);
    }

    private void OnBookmarkTreeDragLeave(object sender, DragEventArgs e)
    {
        RemoveBookmarkDropIndicator();
        StopBookmarkAutoScroll();
    }

    /// <summary>
    /// ドラッグ中のカーソルがツリー表示範囲の上端/下端付近(AutoScrollEdgeMargin以内)にある間、
    /// タイマーで少しずつスクロールし続ける。カーソルがドロップ先の行を外れても、
    /// ツリー描画範囲外(スクロール可能な余地がある方向)へドラッグを続けられるようにするための対応。
    /// </summary>
    private void UpdateBookmarkAutoScroll(DragEventArgs e)
    {
        _bookmarkTreeScrollViewer ??= FindDescendant<ScrollViewer>(BookmarkTreeView);
        if (_bookmarkTreeScrollViewer is null)
        {
            return;
        }

        var position = e.GetPosition(BookmarkTreeView);
        var height = BookmarkTreeView.ActualHeight;

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
        if (_bookmarkAutoScrollTimer is null)
        {
            _bookmarkAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _bookmarkAutoScrollTimer.Tick += (_, _) =>
                _bookmarkTreeScrollViewer?.ScrollToVerticalOffset(_bookmarkTreeScrollViewer.VerticalOffset + _bookmarkAutoScrollStep);
        }

        if (!_bookmarkAutoScrollTimer.IsEnabled)
        {
            _bookmarkAutoScrollTimer.Start();
        }
    }

    private void StopBookmarkAutoScroll() => _bookmarkAutoScrollTimer?.Stop();

    private void OnBookmarkTreeDrop(object sender, DragEventArgs e)
    {
        RemoveBookmarkDropIndicator();
        StopBookmarkAutoScroll();

        if (e.Data.GetData(BookmarkDragFormat) is not BookmarkNodeViewModel dragged)
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
        e.Handled = true;
    }

    /// <summary>
    /// ドラッグ中のカーソル位置から、ドロップ対象ノードと挿入方法(子/兄弟)、
    /// および挿入位置インジケータの描画座標を求める。対象行の外側(空白領域)の場合はnull。
    /// </summary>
    private BookmarkDropPlan? ResolveBookmarkDropPlan(DragEventArgs e)
    {
        // e.OriginalSourceによるヒットテストだと、行内の実要素(タイトル欄・ComboBox等)が無い部分
        // (レベル表示の左側の余白、結合後ページ表示の右側の余白)にカーソルがある場合にヒットする要素が無く、
        // ドロップ対象が見つからなくなる。OnBookmarkTreePreviewMouseLeftButtonDown(行選択)と同様、
        // カーソルのY座標から幾何的に行を探すことで、行の全幅でドロップを受け付けるようにする。
        var targetItem = FindTreeViewItemAtY(BookmarkTreeView, e.GetPosition(BookmarkTreeView).Y);
        if (targetItem?.DataContext is not BookmarkNodeViewModel targetNode)
        {
            return null;
        }

        var headerBorder = FindOwnHeaderBorder(targetItem);
        if (headerBorder is null || headerBorder.ActualHeight <= 0)
        {
            return null;
        }

        var headerTopLeft = headerBorder.TransformToAncestor(BookmarkTreeView).Transform(new Point(0, 0));
        var mouseWithinHeader = e.GetPosition(headerBorder);
        var insertAsChild = mouseWithinHeader.Y < headerBorder.ActualHeight / 2;

        double lineX;
        double lineY;
        if (insertAsChild)
        {
            // 子として挿入: そのノードの一段深い位置、ヘッダー直下(=先頭の子の位置)に線を引く。
            lineX = headerTopLeft.X + BookmarkChildIndent;
            lineY = headerTopLeft.Y + headerBorder.ActualHeight;
        }
        else
        {
            // 兄弟として挿入: そのノードと同じ深さで、展開中の子要素を含めた行全体の下端に線を引く。
            var itemTopLeft = targetItem.TransformToAncestor(BookmarkTreeView).Transform(new Point(0, 0));
            lineX = headerTopLeft.X;
            lineY = itemTopLeft.Y + targetItem.ActualHeight;
        }

        return new BookmarkDropPlan(targetNode, insertAsChild, lineX, lineY);
    }

    private void ShowBookmarkDropIndicator(double x, double y)
    {
        _bookmarkAdornerLayer ??= AdornerLayer.GetAdornerLayer(BookmarkTreeView);
        if (_bookmarkAdornerLayer is null)
        {
            return;
        }

        if (_bookmarkDropAdorner is null)
        {
            _bookmarkDropAdorner = new BookmarkInsertionAdorner(BookmarkTreeView);
            _bookmarkAdornerLayer.Add(_bookmarkDropAdorner);
        }

        _bookmarkDropAdorner.UpdatePosition(x, y, Math.Max(0, BookmarkTreeView.ActualWidth - x));
    }

    private void RemoveBookmarkDropIndicator()
    {
        if (_bookmarkDropAdorner is null)
        {
            return;
        }

        _bookmarkAdornerLayer?.Remove(_bookmarkDropAdorner);
        _bookmarkDropAdorner = null;
    }

    /// <summary>TreeViewItem自身のヘッダー行(HierarchicalDataTemplateのルートBorder)を探す。
    /// 子として描画されたネストTreeViewItemの内部には descend しない。</summary>
    private static Border? FindOwnHeaderBorder(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border { Tag: BookmarkNodeViewModel } border)
            {
                return border;
            }

            if (child is TreeViewItem)
            {
                continue;
            }

            var found = FindOwnHeaderBorder(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnBookmarkTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        LevelCapButton.IsEnabled = GetSelectedBookmarkNode() is { Children.Count: > 0 };
        UpdateLevelButtonsEnabled();
    }

    /// <summary>
    /// しおり行1件分は多数のコントロールを横に並べた幅広の行のため、ウィンドウ幅より広い場合は
    /// 横スクロールバーが表示される(HorizontalScrollBarVisibility="Auto")。この状態で行をクリックすると、
    /// TreeViewItemの既定動作(選択・フォーカス変更に伴うBringIntoView)により行全体(横方向含む)を
    /// 表示しようとして、意図せず横スクロール位置が動いてしまう不具合があった。
    /// クリック直後(選択処理が始まる前)の横スクロール位置を保存しておき、選択・フォーカス変更に伴う
    /// 一連の処理(BringIntoViewや、その後のレイアウト更新)が完了した後のタイミング
    /// (Dispatcher.BeginInvokeでDispatcherPriority.ContextIdleまでキューを空にしてから)に元の位置へ
    /// 復元することで、原因となる個々の処理(既定のBringIntoViewか、SelectBookmarkRowAtYの
    /// item.Focus()か等)を問わず、確実に横スクロール位置を保つ。縦方向は復元しないため、
    /// キーボード操作で画面外の行を選択した場合の縦方向の自動スクロールはこれまでどおり機能する。
    /// </summary>
    private void PreserveBookmarkTreeHorizontalScrollPosition()
    {
        _bookmarkTreeScrollViewer ??= FindDescendant<ScrollViewer>(BookmarkTreeView);
        if (_bookmarkTreeScrollViewer is null)
        {
            return;
        }

        var horizontalOffset = _bookmarkTreeScrollViewer.HorizontalOffset;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            () => _bookmarkTreeScrollViewer?.ScrollToHorizontalOffset(horizontalOffset));
    }

    private void UpdateLevelButtonsEnabled()
    {
        var node = GetSelectedBookmarkNode();
        PromoteLevelButton.IsEnabled = node is not null && ViewModel.BookmarkTree.CanPromoteLevel(node);
        DemoteLevelButton.IsEnabled = node is not null && ViewModel.BookmarkTree.CanDemoteLevel(node);
    }

    private void OnPromoteLevelClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            ViewModel.BookmarkTree.PromoteLevel(node);
            RecomputeTitleColumnWidth();
            UpdateLevelButtonsEnabled();
        }
    }

    private void OnDemoteLevelClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            ViewModel.BookmarkTree.DemoteLevel(node);
            RecomputeTitleColumnWidth();
            UpdateLevelButtonsEnabled();
        }
    }

    private async void OnSetLevelCapClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            await ViewModel.BookmarkTree.SetChildLevelCapAsync(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnAddRootBookmarkClick(object sender, RoutedEventArgs e)
    {
        ViewModel.BookmarkTree.AddRoot();
        RecomputeTitleColumnWidth();
    }

    private void OnAddChildBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            ViewModel.BookmarkTree.AddChild(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnAddSiblingBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            ViewModel.BookmarkTree.AddSiblingAfter(node);
            RecomputeTitleColumnWidth();
        }
    }

    private void OnDeleteBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedBookmarkNode() is { } node)
        {
            ViewModel.BookmarkTree.Remove(node);
            RecomputeTitleColumnWidth();
        }
    }

    private BookmarkNodeViewModel? GetSelectedBookmarkNode() =>
        BookmarkTreeView.SelectedItem as BookmarkNodeViewModel;

    /// <summary>
    /// 結合前ページ数テキストボックスのコンテキストメニュー(リセット)。ContextMenuはプロパティ経由で
    /// TextBoxへ割り当てているため、WPFのInheritanceContextによりMenuItem.DataContextはそのTextBoxの
    /// DataContext(=対象のBookmarkNodeViewModel)へ解決される。対象ノードが属するPDFファイルに
    /// 関係する結合前ページ数の編集を、ファイル単位で一括リセットする(そのノード単体だけでなく、
    /// 同一ファイル内の他のノードへの編集もすべて元へ戻す)。
    /// </summary>
    private void OnResetPreOffsetPageNumberClick(object sender, RoutedEventArgs e)
    {
        if (((MenuItem)sender).DataContext is BookmarkNodeViewModel node)
        {
            ViewModel.BookmarkTree.ResetFilePageNumbers(node);
        }
    }

    /// <summary>
    /// ツリー開閉レベルテキストボックスがフォーカスを失った際に、入力値が数値以外またはツリーに
    /// 含まれない数値であれば空欄へ正規化する(適用自体はBookmarkTreeViewModel.ExpandLevelInputの
    /// 値変更購読が随時行う。WPFのTextBox.Textは既定でUpdateSourceTrigger=LostFocusのため、
    /// 実質的にはこのタイミングで適用と正規化がまとめて行われる)。
    /// </summary>
    private void OnExpandLevelTextBoxLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.BookmarkTree.NormalizeExpandLevelInput();

    /// <summary>
    /// リンク編集画面のしおり一覧をクリックした時の動作。ジャンプ先の指定待ち(PendingSelection)の間は
    /// クリックしたしおりをジャンプ先として選択し、それ以外の場合は該当ページへプレビューをジャンプする。
    /// </summary>
    private void OnLinkEditorBookmarkClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not BookmarkNode bookmark)
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

    /// <summary>連続スクロールプレビューの1ページ分のコンテナがビューポートに入った時
    /// (VirtualizingPanelによるコンテナの初回生成・再利用のいずれでも発生する)に、そのページの
    /// 画像描画をトリガーする。現在ページのコンテナであれば、生成タイミングによってはRedrawLinkOverlayが
    /// 実体化前に呼ばれて何もできなかった可能性があるため、ここでも改めてオーバーレイを描画する。</summary>
    private void OnPageSlotLoaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PdfPageSlotViewModel slot)
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
    private void OnPageSlotUnloaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PdfPageSlotViewModel slot)
        {
            ViewModel.LinkEditor.UnloadPageSlot(slot.PageIndex);
        }
    }

    /// <summary>連続スクロールプレビューの各ページコンテナに設定しているMargin(下方向の余白)。
    /// オフセット計算をXAML側のListBox.ItemContainerStyleと一致させるために使う。</summary>
    private const double PageItemBottomMargin = 4;

    /// <summary>プレビューのスクロール位置から、現在の操作対象とみなすページ(ビューポート内で
    /// 最も表示面積が大きいページ)を求め、CurrentPageIndexへ反映する。これにより、ページ送り
    /// ボタンを使わずスクロールするだけで1ページ目から最終ページまで移動できる。
    /// VisualTreeHelper.HitTestによる検出は、Wpf.Ui(FluentWindow)がScrollViewerを独自の
    /// PassiveScrollViewerへ差し替えており、そのヒットテスト結果が内部コンテンツまで到達せず
    /// 常にPassiveScrollViewer自身で止まってしまうため機能しなかった。全ページ同一の
    /// プレースホルダ高さを前提に、オフセットとビューポート高さから直接算出する。</summary>
    private void OnPdfPreviewScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ComputeMostVisiblePageIndex(e.VerticalOffset, e.ViewportHeight) is not { } pageIndex || pageIndex == ViewModel.LinkEditor.CurrentPageIndex.Value)
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
    /// 最も表示面積が大きいページ番号を算出する。全ページ同一のプレースホルダ高さを前提とする。</summary>
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
    /// WPFのListBox.ScrollIntoViewは「最小限のスクロールで対象を見えるようにする」動作のため、
    /// 対象ページの末尾がビューポート下端に揃ってしまい先頭に揃わないことがある(WPFの既知の挙動)。
    /// さらにScrollIntoViewと直後のScrollToVerticalOffsetを続けて呼ぶと、Wpf.UiのPassiveScrollViewer
    /// (アニメーション付きスクロールを内部で行っている可能性がある)側でどちらか一方の指示が
    /// 反映されないことがあったため、ScrollIntoViewは使わずScrollToVerticalOffsetのみで直接指定する
    /// (対象が仮想化によりまだ実体化されていなくても、VirtualizingStackPanelはスクロール位置に応じて
    /// レイアウト時にコンテナを生成するため、これだけで数千ページ先へのジャンプにも対応できる)。</summary>
    private void ScrollToPage(int pageIndex)
    {
        var linkEditor = ViewModel.LinkEditor;
        if (pageIndex < 0 || pageIndex >= linkEditor.PageSlots.Count)
        {
            return;
        }

        if (FindDescendant<ScrollViewer>(PdfPageListBox) is { } scrollViewer)
        {
            var offset = pageIndex * (linkEditor.PlaceholderHeight.Value + PageItemBottomMargin);
            scrollViewer.ScrollToVerticalOffset(offset);
        }

        RedrawLinkOverlay();
    }

    private void OnPdfPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hitLayer = (UIElement)sender;
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
        hitLayer.CaptureMouse();
        linkEditor.BeginTextSelection(pdfX, pdfY);
        RedrawLinkOverlay();
    }

    private void OnPdfPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelectingLinkText)
        {
            return;
        }

        var hitLayer = (UIElement)sender;
        var linkEditor = ViewModel.LinkEditor;
        var position = e.GetPosition(hitLayer);
        var (pdfX, pdfY) = PdfCoordinateMapper.ToPdf(position.X, position.Y, linkEditor.PageHeight.Value, linkEditor.ZoomScale.Value);
        linkEditor.UpdateTextSelection(pdfX, pdfY);
        RedrawLinkOverlay();
    }

    private void OnPdfPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelectingLinkText)
        {
            return;
        }

        _isSelectingLinkText = false;
        ((UIElement)sender).ReleaseMouseCapture();
        ViewModel.LinkEditor.EndTextSelection();
        RedrawLinkOverlay();
    }

    /// <summary>
    /// ドラッグ中にマウスキャプチャが外部要因(Alt+Tab・別ウィンドウのモーダル表示等)で失われた場合の
    /// 保険。MouseLeftButtonUpが発火しないまま_isSelectingLinkTextがtrueに残ると、ボタンを押していない
    /// 通常のマウス移動までUpdateTextSelectionへ伝わり続け、次の意図しないクリックで不正な選択範囲が
    /// 確定してしまう。ReleaseMouseCapture()自身が発火させるLostMouseCaptureは、その時点で既に
    /// _isSelectingLinkTextをfalseにしてから呼んでいるため、下のガードで正しく無視される。
    /// </summary>
    private void OnPdfPreviewLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isSelectingLinkText)
        {
            return;
        }

        _isSelectingLinkText = false;
        ViewModel.LinkEditor.CancelPendingSelection();
        RedrawLinkOverlay();
    }

    /// <summary>現在ページのコンテナが実体化されている場合に限り、そのコンテナ内のLinkOverlayCanvasを返す。
    /// 連続スクロール表示では、現在ページがスクロール未到達等でまだ仮想化により実体化されていないことがあり、
    /// その間は描画対象が存在しないためnullを返す(コンテナが実体化された時にOnPageSlotLoadedから
    /// 改めて呼ばれる)。</summary>
    private Canvas? FindCurrentPageOverlayCanvas()
    {
        var container = PdfPageListBox.ItemContainerGenerator.ContainerFromIndex(ViewModel.LinkEditor.CurrentPageIndex.Value);
        return container is null ? null : FindDescendant<Canvas>(container);
    }

    private static readonly SolidColorBrush ExistingLinkFill = new(Color.FromArgb(60, 0, 200, 0));
    private static readonly SolidColorBrush LiveSelectionFill = new(Color.FromArgb(80, 30, 144, 255));

    /// <summary>
    /// 現在ページに属する確定済みリンクのホットスポット(緑)と、ドラッグ中の選択範囲(青、行ごとに
    /// 実際の文字の外接矩形を使う)を描画し直す。両者は色・線種で視覚的に区別する。ドラッグ中の
    /// 選択範囲をLinkEditorViewModel.LiveSelectionLineRects(単純な始点〜終点の外接矩形ではなく、
    /// GroupLettersIntoLineRectsで実際に選択される文字の行ごとの矩形)から取得することで、
    /// ズーム変更・ページ高さ変更等どの経路からRedrawLinkOverlayが呼ばれても、ドラッグ中の
    /// 選択表示を消してしまうことなく正しく再描画できる。
    /// </summary>
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
            canvas.Children.Add(CreateOverlayRect(pixelRect, ExistingLinkFill, Brushes.Green));
        }

        // ドラッグ中(LiveSelectionLineRectsが非空)はその内容を、ドラッグ終了後はPendingSelection
        // (現在ページが選択元ページと一致する場合のみ)を、選択中・確定待ちの範囲として描画する。
        // これにより、ジャンプ先を選ぶ操作の途中で別ページへ移動しても、選択元のページへ戻れば
        // 可視化が復元され、リンクを確定またはキャンセルするまで一貫して確認できる。
        var selectionRects = linkEditor.LiveSelectionLineRects.Value.Count > 0
            ? linkEditor.LiveSelectionLineRects.Value
            : linkEditor.PendingSelection.Value is { } pending && pending.SourcePageIndex == currentPage
                ? pending.LineRects
                : [];

        foreach (var rect in selectionRects)
        {
            var pixelRect = PdfCoordinateMapper.ToPixelRect(rect, pageHeight, scale);
            canvas.Children.Add(CreateOverlayRect(pixelRect, LiveSelectionFill, Brushes.DodgerBlue));
        }
    }

    private static System.Windows.Shapes.Rectangle CreateOverlayRect(PdfRect pixelRect, Brush fill, Brush stroke)
    {
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = pixelRect.Right - pixelRect.Left,
            Height = pixelRect.Bottom - pixelRect.Top,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(rect, pixelRect.Left);
        Canvas.SetTop(rect, pixelRect.Top);
        return rect;
    }

    /// <summary>リンク一覧の「表示」ボタン: そのリンクのホットスポットがあるページへプレビューをジャンプする(動作確認用)。</summary>
    private void OnLinkGroupJumpClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is LinkGroupInfo group)
        {
            ViewModel.LinkEditor.JumpToPageCommand.Execute(group.SourcePageIndex);
        }
    }

    /// <summary>リンク一覧の「編集」ボタン: 既存リンクを一旦外し、同じホットスポットのままジャンプ先を選び直せる状態にする。</summary>
    private void OnLinkGroupEditClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is LinkGroupInfo group)
        {
            ViewModel.LinkEditor.EditLinkGroupCommand.Execute(group.GroupId);
        }
    }

    private void OnLinkGroupDeleteClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is LinkGroupInfo group)
        {
            ViewModel.LinkEditor.DeleteLinkGroupCommand.Execute(group.GroupId);
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var found = FindDescendant<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
