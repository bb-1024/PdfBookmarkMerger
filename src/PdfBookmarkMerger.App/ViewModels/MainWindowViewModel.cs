using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// メインウィンドウ全体を統括するViewModel。
/// 手順1(ファイル指定)→手順2(しおり抽出)→手順3(しおり編集)→手順4(結合・保存)の流れを制御する。
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IPdfMetadataService _metadataService;
    private readonly IPdfMergeService _mergeService;
    private readonly IDialogService _dialogService;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly Dictionary<Guid, PdfFileMetadata> _metadataByFileId = [];

    public MainWindowViewModel(
        FileListViewModel fileList,
        BookmarkTreeViewModel bookmarkTree,
        IPdfMetadataService metadataService,
        IPdfMergeService mergeService,
        IDialogService dialogService,
        IUserSettingsService userSettings,
        ILogger<MainWindowViewModel> logger)
    {
        FileList = fileList;
        BookmarkTree = bookmarkTree;
        _metadataService = metadataService;
        _mergeService = mergeService;
        _dialogService = dialogService;
        _userSettings = userSettings;
        _logger = logger;

        Step = new ReactivePropertySlim<WorkflowStep>(WorkflowStep.SelectFiles).AddTo(Disposables);
        IsBusy = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        StatusMessage = new ReactivePropertySlim<string>("結合したいPDFファイルを追加してください。").AddTo(Disposables);

        var canConfirm = FileList.HasFiles.CombineLatest(IsBusy, (hasFiles, busy) => hasFiles && !busy);
        ConfirmFilesCommand = new AsyncReactiveCommand(canConfirm).AddTo(Disposables);
        ConfirmFilesCommand.Subscribe(async () => await ConfirmFilesAsync()).AddTo(Disposables);

        var isEditingBookmarks = Step.Select(s => s == WorkflowStep.EditBookmarks);
        var canMerge = isEditingBookmarks.CombineLatest(IsBusy, (editing, busy) => editing && !busy);
        MergeCommand = new AsyncReactiveCommand(canMerge).AddTo(Disposables);
        MergeCommand.Subscribe(async () => await MergeAsync()).AddTo(Disposables);

        var canGoBack = isEditingBookmarks.CombineLatest(IsBusy, (editing, busy) => editing && !busy);
        BackToFileListCommand = new ReactiveCommand(canGoBack).AddTo(Disposables);
        BackToFileListCommand.Subscribe(() => Step.Value = WorkflowStep.SelectFiles).AddTo(Disposables);

        AddFilesViaDialogCommand = new AsyncReactiveCommand(IsBusy.Select(b => !b)).AddTo(Disposables);
        AddFilesViaDialogCommand.Subscribe(async () =>
        {
            var files = await _dialogService.ShowOpenPdfFilesDialogAsync();
            FileList.AddPaths(files);
        }).AddTo(Disposables);

        AddFolderViaDialogCommand = new AsyncReactiveCommand(IsBusy.Select(b => !b)).AddTo(Disposables);
        AddFolderViaDialogCommand.Subscribe(async () =>
        {
            var folder = await _dialogService.ShowOpenFolderDialogAsync();
            if (folder is not null)
            {
                FileList.AddPaths([folder]);
            }
        }).AddTo(Disposables);

        OpenSettingsCommand = new AsyncReactiveCommand(IsBusy.Select(b => !b)).AddTo(Disposables);
        OpenSettingsCommand.Subscribe(async () =>
        {
            var updated = await _dialogService.ShowSettingsDialogAsync(_userSettings.Current);
            if (updated is not null)
            {
                await _userSettings.SaveAsync(updated);
            }
        }).AddTo(Disposables);
    }

    public FileListViewModel FileList { get; }

    public BookmarkTreeViewModel BookmarkTree { get; }

    public ReactivePropertySlim<WorkflowStep> Step { get; }

    public ReactivePropertySlim<bool> IsBusy { get; }

    public ReactivePropertySlim<string> StatusMessage { get; }

    public AsyncReactiveCommand ConfirmFilesCommand { get; }

    public AsyncReactiveCommand MergeCommand { get; }

    public ReactiveCommand BackToFileListCommand { get; }

    public AsyncReactiveCommand AddFilesViaDialogCommand { get; }

    public AsyncReactiveCommand AddFolderViaDialogCommand { get; }

    public AsyncReactiveCommand OpenSettingsCommand { get; }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task ConfirmFilesAsync()
    {
        IsBusy.Value = true;
        StatusMessage.Value = "ページ数・しおり情報を読み込んでいます...";
        _metadataByFileId.Clear();

        try
        {
            var files = FileList.Files.ToList();
            var failedCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var metadata = await _metadataService.ReadMetadataAsync(file.Model);
                    file.ApplyPageCount(metadata.PageCount);
                    _metadataByFileId[file.Id] = metadata;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    file.MarkLoadFailed();
                    _logger.LogError(ex, "PDFメタデータの読み込みに失敗しました: {File}", file.FilePath);
                    _dialogService.ShowError("読み込みエラー", $"'{file.FileName}' の読み込みに失敗したためスキップします。\n{ex.Message}");
                }
            }

            var orderedFiles = files.Where(f => _metadataByFileId.ContainsKey(f.Id)).Select(f => f.Model).ToList();

            if (orderedFiles.Count == 0)
            {
                StatusMessage.Value = "読み込めるPDFファイルがありませんでした。ファイルを確認してください。";
                return;
            }

            var effectiveBookmarks = MissingBookmarkFallback.ResolveEffectiveBookmarks(orderedFiles, _metadataByFileId);
            var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks(orderedFiles, effectiveBookmarks, _metadataByFileId);
            var fileNames = orderedFiles.ToDictionary(f => f.Id, f => f.FileName);

            BookmarkTree.Load(merged, fileNames);
            Step.Value = WorkflowStep.EditBookmarks;

            StatusMessage.Value = failedCount == 0
                ? $"{orderedFiles.Count}ファイルを読み込みました。しおりを編集し、結合を実行してください。"
                : $"{orderedFiles.Count}ファイルを読み込みました({failedCount}ファイルはスキップされました)。";
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task MergeAsync()
    {
        // ConfirmFilesAsyncでメタデータ読み込みに失敗したファイルは、しおりツリーに含まれていないため
        // ここでも除外する。含めてしまうと、結合失敗やページオフセットのずれの原因になる。
        var mergeTargetFiles = FileList.Files.Where(f => !f.LoadFailed.Value).ToList();
        var firstFile = mergeTargetFiles.FirstOrDefault();
        var defaultProperties = firstFile is not null && _metadataByFileId.TryGetValue(firstFile.Id, out var meta)
            ? meta.Properties.Clone()
            : PdfDocumentPropertiesModel.CreateEmpty();

        // 保存先の既定値: 1番目のファイルの格納フォルダ・「{1番目のファイル名}_merged.pdf」。
        var suggestedFileName = firstFile is not null
            ? $"{Path.GetFileNameWithoutExtension(firstFile.FilePath)}_merged.pdf"
            : "結合結果.pdf";
        var initialDirectory = firstFile is not null
            ? Path.GetDirectoryName(firstFile.FilePath)
            : _userSettings.Current.LastOutputDirectory;

        var outputPath = await _dialogService.ShowSaveMergedPdfDialogAsync(suggestedFileName, initialDirectory);
        if (outputPath is null)
        {
            return;
        }

        PdfDocumentPropertiesModel properties;
        if (_userSettings.Current.ShowPropertiesDialogOnMerge)
        {
            var edited = await _dialogService.ShowPropertiesDialogAsync(defaultProperties);
            if (edited is null)
            {
                return;
            }

            properties = edited;
        }
        else
        {
            properties = defaultProperties;
        }

        IsBusy.Value = true;
        StatusMessage.Value = "PDFを結合しています...";

        try
        {
            var request = new PdfMergeRequest
            {
                Files = mergeTargetFiles.Select(f => f.Model).ToList(),
                Bookmarks = BookmarkTree.ToModel(),
                Properties = properties,
                OutputPath = outputPath,
            };

            await _mergeService.MergeAsync(request);

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                var updated = new PdfBookmarkMergerOptions
                {
                    LastOutputDirectory = outputDirectory,
                    WindowWidth = _userSettings.Current.WindowWidth,
                    WindowHeight = _userSettings.Current.WindowHeight,
                    ThemeMode = _userSettings.Current.ThemeMode,
                    ShowPropertiesDialogOnMerge = _userSettings.Current.ShowPropertiesDialogOnMerge,
                };
                await _userSettings.SaveAsync(updated);
            }

            StatusMessage.Value = $"結合が完了しました: {outputPath}";
            _dialogService.ShowInfo("結合完了", $"PDFファイルを結合しました。\n{outputPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF結合に失敗しました: {OutputPath}", outputPath);
            _dialogService.ShowError("結合エラー", $"PDFの結合に失敗しました。\n{ex.Message}");
        }
        finally
        {
            IsBusy.Value = false;
        }
    }
}
