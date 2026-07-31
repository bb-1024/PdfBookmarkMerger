using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.WpfApp.Controls;

namespace PdfBookmarkMerger.WpfApp;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string FileDragFormat = "PdfBookmarkMerger.PdfFileEntryViewModel";
    private const string BookmarkDragFormat = "PdfBookmarkMerger.BookmarkNodeViewModel";

    /// <summary>しおり行のドロップ判定で、上半分/下半分どちらにカーソルがあるかを示す境界の目安インデント量(px)。</summary>
    private const double BookmarkChildIndent = 19;

    private Point _fileDragStart;
    private Point _bookmarkDragStart;

    private AdornerLayer? _bookmarkAdornerLayer;
    private BookmarkInsertionAdorner? _bookmarkDropAdorner;
    private PlaceholderTextAdorner? _fileListPlaceholderAdorner;

    /// <summary>IsBusyが5秒以上継続した場合にのみ、詳細進捗(BusyDetailText)を表示するためのタイマー。</summary>
    private readonly DispatcherTimer _busyDetailTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private readonly record struct BookmarkDropPlan(BookmarkNodeViewModel TargetNode, bool InsertAsChild, double LineX, double LineY);

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        _busyDetailTimer.Tick += OnBusyDetailTimerTick;

        Loaded += OnMainWindowLoaded;
    }

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        // ファイル一覧が空の間、AdornerLayer経由でヒントテキストを重ねる。
        // ListBoxの可視ツリー自体は一切変更しないため、D&Dのヒットテストに影響しない。
        ViewModel.FileList.HasFiles.Subscribe(_ => UpdateFileListPlaceholder());

        // しおり編集画面に入るたびに、タイトル列の幅を現在のタイトル群に合わせて再計算する。
        ViewModel.Step.Subscribe(step =>
        {
            if (step == WorkflowStep.EditBookmarks)
            {
                RecomputeTitleColumnWidth();
            }
        });

        ViewModel.IsBusy.Subscribe(OnIsBusyChanged);
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
                FileListBox, "PDFファイルまたはフォルダをここにドラッグ&ドロップ", foreground);
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
        if (FileListBox.SelectedItem is PdfFileEntryViewModel item)
        {
            ViewModel.FileList.MoveUp(item);
        }
    }

    private void OnMoveFileDownClick(object sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is PdfFileEntryViewModel item)
        {
            ViewModel.FileList.MoveDown(item);
        }
    }

    // ---- しおりツリー: D&Dによる並べ替え・再親子付け ----

    private void OnBookmarkTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _bookmarkDragStart = e.GetPosition(null);
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
    }

    private void OnBookmarkTreeDragLeave(object sender, DragEventArgs e) => RemoveBookmarkDropIndicator();

    private void OnBookmarkTreeDrop(object sender, DragEventArgs e)
    {
        RemoveBookmarkDropIndicator();

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
        var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
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
}
