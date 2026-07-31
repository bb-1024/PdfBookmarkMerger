using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 結合対象PDFファイル一覧(手順1)のViewModel。D&D/ダイアログでの追加、並べ替え、削除を扱う。
/// </summary>
public sealed class FileListViewModel : ViewModelBase
{
    private readonly IPdfFileCollectorService _collector;
    private readonly IPdfMetadataService _metadataService;
    private readonly ILogger<FileListViewModel> _logger;

    public FileListViewModel(
        IPdfFileCollectorService collector,
        IPdfMetadataService metadataService,
        ILogger<FileListViewModel> logger)
    {
        _collector = collector;
        _metadataService = metadataService;
        _logger = logger;

        HasFiles = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
    }

    public ObservableCollection<PdfFileEntryViewModel> Files { get; } = [];

    public ReactivePropertySlim<bool> HasFiles { get; }

    /// <summary>
    /// D&Dまたはダイアログで渡されたパス群(ファイル/フォルダ混在可)を一覧に追加する。
    /// ページ数は追加直後に非同期で読み込み、一覧にすぐ表示する。
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var pdfFiles = _collector.ExpandToPdfFilePaths(paths);
        foreach (var path in pdfFiles)
        {
            var entry = new PdfFileEntry { FilePath = path };
            var itemViewModel = new PdfFileEntryViewModel(entry);
            Files.Add(itemViewModel);
            _logger.LogInformation("結合対象に追加: {Path}", path);

            _ = LoadPageCountAsync(itemViewModel);
        }

        HasFiles.Value = Files.Count > 0;
    }

    private async Task LoadPageCountAsync(PdfFileEntryViewModel item)
    {
        try
        {
            var pageCount = await _metadataService.ReadPageCountAsync(item.FilePath);
            item.ApplyPageCount(pageCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ページ数の取得に失敗しました: {Path}", item.FilePath);
        }
    }

    public void Remove(PdfFileEntryViewModel item)
    {
        Files.Remove(item);
        HasFiles.Value = Files.Count > 0;
    }

    /// <summary>
    /// 選択中の連続した1件以上のファイルを、選択順序を保ったまま1つ上へまとめて移動する。
    /// 選択が非連続(間に非選択ファイルを挟む)の場合、またはすでに先頭に達している場合は何もしない。
    /// </summary>
    public void MoveSelectionUp(IReadOnlyList<PdfFileEntryViewModel> selected)
    {
        var indices = ResolveContiguousSortedIndices(selected);
        if (indices is null || indices[0] <= 0)
        {
            return;
        }

        // 選択ブロックの直前にある1件を、ブロックの直後へ移動する。
        // これはブロック全体を1つ上へずらすのと等価だが、ObservableCollection.Moveの1回呼び出しで済む。
        Files.Move(indices[0] - 1, indices[^1]);
    }

    /// <summary>
    /// 選択中の連続した1件以上のファイルを、選択順序を保ったまま1つ下へまとめて移動する。
    /// 選択が非連続(間に非選択ファイルを挟む)の場合、またはすでに末尾に達している場合は何もしない。
    /// </summary>
    public void MoveSelectionDown(IReadOnlyList<PdfFileEntryViewModel> selected)
    {
        var indices = ResolveContiguousSortedIndices(selected);
        if (indices is null || indices[^1] >= Files.Count - 1)
        {
            return;
        }

        // 選択ブロックの直後にある1件を、ブロックの直前へ移動する(上記の対称版)。
        Files.Move(indices[^1] + 1, indices[0]);
    }

    /// <summary>
    /// 現在の選択に対し、上へ/下へボタンをそれぞれ活性化してよいかを返す。
    /// 選択が空、または非連続の場合は両方とも不可(false, false)。
    /// </summary>
    public (bool CanMoveUp, bool CanMoveDown) GetMoveAvailability(IReadOnlyList<PdfFileEntryViewModel> selected)
    {
        var indices = ResolveContiguousSortedIndices(selected);
        return indices is null ? (false, false) : (indices[0] > 0, indices[^1] < Files.Count - 1);
    }

    /// <summary>
    /// 選択されたファイル群がFiles内で連続した並びを構成しているかを確認し、連続していれば
    /// 昇順インデックス一覧を返す。空選択・非連続選択・未所属の項目を含む場合はnullを返す。
    /// </summary>
    private List<int>? ResolveContiguousSortedIndices(IReadOnlyList<PdfFileEntryViewModel> selected)
    {
        if (selected.Count == 0)
        {
            return null;
        }

        var indices = selected.Select(Files.IndexOf).OrderBy(i => i).ToList();
        if (indices[0] < 0)
        {
            return null;
        }

        for (var i = 1; i < indices.Count; i++)
        {
            if (indices[i] != indices[i - 1] + 1)
            {
                return null;
            }
        }

        return indices;
    }

    /// <summary>指定インデックスへ移動する(TreeView/ListView上でのD&D並べ替え用)。</summary>
    public void MoveTo(PdfFileEntryViewModel item, int newIndex)
    {
        var oldIndex = Files.IndexOf(item);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Files.Count - 1);
        if (oldIndex != newIndex)
        {
            Files.Move(oldIndex, newIndex);
        }
    }
}
