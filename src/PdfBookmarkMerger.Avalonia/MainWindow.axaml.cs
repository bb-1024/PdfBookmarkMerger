using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PdfBookmarkMerger.App.ViewModels;

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
        });

        _busyDetailTimer.Tick += OnBusyDetailTimerTick;
        ViewModel.IsBusy.Subscribe(OnIsBusyChanged);

        // Undo(元に戻す)はRootNodes全体を作り直すため、タイトル列幅もあわせて再計算する。
        // VM側のUndoCommand.Subscribe(Undo)(RootNodes再構築)が先に完了してから呼ばれる。
        ViewModel.BookmarkTree.UndoCommand.Subscribe(RecomputeTitleColumnWidth);

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

        ViewModel.FileList.Files.CollectionChanged += (_, _) => UpdateFileMoveButtonsEnabled();
        UpdateFileMoveButtonsEnabled();

        // ListBox/TreeView自身の選択処理(SelectingItemsControlの既定の内部処理)がPointerPressedを
        // Bubbleフェーズで先取りしてHandled=trueにするため、通常のXAMLイベント購読(Bubble)では
        // D&D開始検知用のハンドラに一切イベントが届かない。WPF版がPreviewMouseLeftButtonDown
        // (Tunnelフェーズ)を使っているのと同様、Tunnelフェーズで明示的に購読することで、
        // 既定の選択処理より先に(確実に)イベントを受け取れるようにする。
        FileListBox.AddHandler(PointerPressedEvent, OnFileListPointerPressed, RoutingStrategies.Tunnel);
        BookmarkTreeView.AddHandler(PointerPressedEvent, OnBookmarkTreePointerPressed, RoutingStrategies.Tunnel);
    }

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
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
        _bookmarkPressedNode = item?.DataContext as BookmarkNodeViewModel;
        _bookmarkPressedArgs = _bookmarkPressedNode is not null ? e : null;
        Serilog.Log.Information("[DND-DIAG] PointerPressed source={Source} item={Item} node={Node}", e.Source?.GetType().Name, item is not null, _bookmarkPressedNode?.Title.Value);

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

        Serilog.Log.Information("[DND-DIAG] Starting DoDragDropAsync for {Title}", dragged.Title.Value);
        try
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(BookmarkDragFormat, dragged));
            var result = await DragDrop.DoDragDropAsync(pressArgs, dataTransfer, DragDropEffects.Move);
            Serilog.Log.Information("[DND-DIAG] DoDragDropAsync completed, result={Result}", result);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[DND-DIAG] DoDragDropAsync threw");
            throw;
        }
        finally
        {
            _bookmarkDragInProgress = false;
        }
    }

    private void OnBookmarkTreeDragOver(object? sender, DragEventArgs e)
    {
        Serilog.Log.Information("[DND-DIAG] OnBookmarkTreeDragOver fired, source={Source}", e.Source?.GetType().Name);
        if (e.DataTransfer.Contains(BookmarkDragFormat))
        {
            e.DragEffects = DragDropEffects.Move;
        }

        var plan = ResolveBookmarkDropPlan(e);
        Serilog.Log.Information("[DND-DIAG] ResolveBookmarkDropPlan -> {Plan}", plan is null ? "null" : $"Target={plan.Value.TargetNode.Title.Value} InsertAsChild={plan.Value.InsertAsChild}");
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
}
