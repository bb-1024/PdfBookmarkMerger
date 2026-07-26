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

    public void MoveUp(PdfFileEntryViewModel item)
    {
        var index = Files.IndexOf(item);
        if (index > 0)
        {
            Files.Move(index, index - 1);
        }
    }

    public void MoveDown(PdfFileEntryViewModel item)
    {
        var index = Files.IndexOf(item);
        if (index >= 0 && index < Files.Count - 1)
        {
            Files.Move(index, index + 1);
        }
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
