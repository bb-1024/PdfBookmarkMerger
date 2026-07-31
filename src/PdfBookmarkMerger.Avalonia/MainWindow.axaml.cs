using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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

    private PointerPressedEventArgs? _filePressedArgs;
    private PdfFileEntryViewModel? _filePressedEntry;
    private bool _fileDragInProgress;

    private PointerPressedEventArgs? _bookmarkPressedArgs;
    private BookmarkNodeViewModel? _bookmarkPressedNode;
    private bool _bookmarkDragInProgress;

    /// <summary>IsBusyが5秒以上継続した場合にのみ、詳細進捗(BusyDetailText)を表示するためのタイマー。</summary>
    private readonly DispatcherTimer _busyDetailTimer = new() { Interval = TimeSpan.FromSeconds(5) };

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
        if (FileListBox.SelectedItem is PdfFileEntryViewModel item)
        {
            ViewModel.FileList.MoveUp(item);
        }
    }

    private void OnMoveFileDownClick(object? sender, RoutedEventArgs e)
    {
        if (FileListBox.SelectedItem is PdfFileEntryViewModel item)
        {
            ViewModel.FileList.MoveDown(item);
        }
    }

    // ---- しおりツリー: D&Dによる並べ替え・再親子付け ----

    private void OnBookmarkTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
        _bookmarkPressedNode = item?.DataContext as BookmarkNodeViewModel;
        _bookmarkPressedArgs = _bookmarkPressedNode is not null ? e : null;
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
    }

    private void OnBookmarkTreeDrop(object? sender, DragEventArgs e)
    {
        var dragged = e.DataTransfer.TryGetValue(BookmarkDragFormat);
        if (dragged is null)
        {
            return;
        }

        var targetItem = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
        var targetNode = targetItem?.DataContext as BookmarkNodeViewModel;

        if (targetNode is not null)
        {
            ViewModel.BookmarkTree.Move(dragged, targetNode, targetNode.Children.Count);
        }
        else
        {
            ViewModel.BookmarkTree.Move(dragged, null, ViewModel.BookmarkTree.RootNodes.Count);
        }

        RecomputeTitleColumnWidth();
    }

    private void OnBookmarkTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LevelCapButton.IsEnabled = BookmarkTreeView.SelectedItem is BookmarkNodeViewModel { Children.Count: > 0 };
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
