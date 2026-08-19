using System.Collections.Concurrent;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
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
    private readonly IBookmarkSettingsExportService _bookmarkSettingsExportService;
    private readonly IDialogService _dialogService;
    private readonly IUserSettingsService _userSettings;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly Dictionary<Guid, PdfFileMetadata> _metadataByFileId = [];

    public MainWindowViewModel(
        FileListViewModel fileList,
        BookmarkTreeViewModel bookmarkTree,
        LinkEditorViewModel linkEditor,
        IPdfMetadataService metadataService,
        IPdfMergeService mergeService,
        IBookmarkSettingsExportService bookmarkSettingsExportService,
        IDialogService dialogService,
        IUserSettingsService userSettings,
        ILogger<MainWindowViewModel> logger)
    {
        FileList = fileList;
        BookmarkTree = bookmarkTree;
        LinkEditor = linkEditor;
        _metadataService = metadataService;
        _mergeService = mergeService;
        _bookmarkSettingsExportService = bookmarkSettingsExportService;
        _dialogService = dialogService;
        _userSettings = userSettings;
        _logger = logger;

        Step = new ReactivePropertySlim<WorkflowStep>(WorkflowStep.SelectFiles).AddTo(Disposables);
        IsBusy = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        StatusMessage = new ReactivePropertySlim<string>(Strings.StatusReady).AddTo(Disposables);
        BusyProgress = new ReactivePropertySlim<BusyProgressInfo?>(null).AddTo(Disposables);
        ShowMergeAndEditLinksButton = new ReactivePropertySlim<bool>(_userSettings.Current.ShowMergeAndEditLinksButton).AddTo(Disposables);

        // しおりが大量にある状態での編集・追加・削除・元に戻す操作は、BookmarkTree内部で
        // 結合前ページ数の再計算(BookmarkTreeViewModel.RecomputeAllPageNumberDisplaysAsync)を
        // 伴いBookmarkTree.IsBusyがtrueになりうる。専用のUIを新設する代わりに、既存の処理中
        // オーバーレイ・CanExecuteゲート(canEdit等、下記)をそのまま再利用できるよう、
        // BookmarkTree側のIsBusy/BusyProgressをこちらのIsBusy/BusyProgressへ転送する。
        string? statusMessageBeforeBookmarkTreeBusy = null;
        BookmarkTree.IsBusy.Subscribe(busy =>
        {
            if (busy)
            {
                statusMessageBeforeBookmarkTreeBusy = StatusMessage.Value;
                StatusMessage.Value = Strings.StatusUpdatingBookmarkTree;
            }
            else if (statusMessageBeforeBookmarkTreeBusy is not null)
            {
                StatusMessage.Value = statusMessageBeforeBookmarkTreeBusy;
                statusMessageBeforeBookmarkTreeBusy = null;
            }

            IsBusy.Value = busy;
        }).AddTo(Disposables);
        BookmarkTree.BusyProgress.Subscribe(p => BusyProgress.Value = p).AddTo(Disposables);

        // リンク編集画面でのページ描画中も、しおり編集時と同じ処理中オーバーレイ・CanExecuteゲートを
        // 再利用できるよう、LinkEditor側のIsBusyをこちらへ転送する(BookmarkTreeと同じ仕組み)。
        LinkEditor.IsBusy.Subscribe(busy => IsBusy.Value = busy).AddTo(Disposables);

        var canConfirm = FileList.HasFiles.CombineLatest(IsBusy, (hasFiles, busy) => hasFiles && !busy);
        ConfirmFilesCommand = new AsyncReactiveCommand(canConfirm).AddTo(Disposables);
        ConfirmFilesCommand.Subscribe(async () => await ConfirmFilesAsync()).AddTo(Disposables);

        var isEditingBookmarks = Step.Select(s => s == WorkflowStep.EditBookmarks);
        var canEdit = isEditingBookmarks.CombineLatest(IsBusy, (editing, busy) => editing && !busy);

        // 結合前ページ数が編集されている間、結合後PDFの実際のページ位置が画面表示・書き出し内容と
        // 食い違うため「結合してPDFを保存」を非活性化する。不整合(ページ数が1未満)が発生している間は
        // 「しおり設定ファイルを保存」も非活性化する。
        var canMerge = canEdit.CombineLatest(BookmarkTree.HasPageNumberEdits, (editable, hasEdits) => editable && !hasEdits);
        MergeCommand = new AsyncReactiveCommand(canMerge).AddTo(Disposables);
        MergeCommand.Subscribe(async () => await MergeAsync()).AddTo(Disposables);

        MergeAndEditLinksCommand = new AsyncReactiveCommand(canMerge).AddTo(Disposables);
        MergeAndEditLinksCommand.Subscribe(async () => await MergeAndEditLinksAsync()).AddTo(Disposables);

        var canSaveBookmarkSettings = canEdit.CombineLatest(BookmarkTree.HasPageNumberInconsistency, (editable, hasInconsistency) => editable && !hasInconsistency);
        SaveBookmarkSettingsCommand = new AsyncReactiveCommand(canSaveBookmarkSettings).AddTo(Disposables);
        SaveBookmarkSettingsCommand.Subscribe(async () => await SaveBookmarkSettingsAsync()).AddTo(Disposables);

        BackToFileListCommand = new ReactiveCommand(canEdit).AddTo(Disposables);
        BackToFileListCommand.Subscribe(() => Step.Value = WorkflowStep.SelectFiles).AddTo(Disposables);

        var isEditingLinks = Step.Select(s => s == WorkflowStep.EditLinks);
        var canBackToBookmarks = isEditingLinks.CombineLatest(IsBusy, (editing, busy) => editing && !busy);
        BackToBookmarksCommand = new ReactiveCommand(canBackToBookmarks).AddTo(Disposables);
        BackToBookmarksCommand.Subscribe(() => Step.Value = WorkflowStep.EditBookmarks).AddTo(Disposables);

        var canFinishLinkEditing = isEditingLinks.CombineLatest(IsBusy, (editing, busy) => editing && !busy);
        FinishLinkEditingCommand = new AsyncReactiveCommand(canFinishLinkEditing).AddTo(Disposables);
        FinishLinkEditingCommand.Subscribe(async () => await FinishLinkEditingAsync()).AddTo(Disposables);

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
        OpenSettingsCommand.Subscribe(async () => await OpenSettingsAsync()).AddTo(Disposables);
    }

    public FileListViewModel FileList { get; }

    public BookmarkTreeViewModel BookmarkTree { get; }

    public LinkEditorViewModel LinkEditor { get; }

    public ReactivePropertySlim<WorkflowStep> Step { get; }

    public ReactivePropertySlim<bool> IsBusy { get; }

    public ReactivePropertySlim<string> StatusMessage { get; }

    /// <summary>
    /// IsBusy中の詳細進捗(完了/総数・処理中のファイル名)。IsBusyがfalseの間はnull。
    /// UI側は、処理開始から5秒以上経過してから初めてこの内容を表示する運用とする(短時間処理での表示チラつき防止)。
    /// </summary>
    public ReactivePropertySlim<BusyProgressInfo?> BusyProgress { get; }

    /// <summary>
    /// しおり編集画面に「結合してリンク編集へ進む」ボタンを表示するかどうか。設定ダイアログでの
    /// 変更が、ダイアログを閉じた時点でこのプロパティ経由でボタンの表示・非表示へ即座に反映される
    /// (Language設定と異なり、ウィンドウの再構築は不要)。
    /// </summary>
    public ReactivePropertySlim<bool> ShowMergeAndEditLinksButton { get; }

    /// <summary>
    /// 大量ファイル読み込み時の並列実行数の上限。CPUコア数に連動しつつ、
    /// 極端な同時オープンによるスレッドプール枯渇・I/O競合を避けるため上限を設ける。
    /// </summary>
    private static readonly int MaxParallelLoad = Math.Clamp(Environment.ProcessorCount, 1, 8);

    public AsyncReactiveCommand ConfirmFilesCommand { get; }

    public AsyncReactiveCommand MergeCommand { get; }

    public AsyncReactiveCommand MergeAndEditLinksCommand { get; }

    public AsyncReactiveCommand SaveBookmarkSettingsCommand { get; }

    public ReactiveCommand BackToFileListCommand { get; }

    public ReactiveCommand BackToBookmarksCommand { get; }

    public AsyncReactiveCommand FinishLinkEditingCommand { get; }

    public AsyncReactiveCommand AddFilesViaDialogCommand { get; }

    public AsyncReactiveCommand AddFolderViaDialogCommand { get; }

    public AsyncReactiveCommand OpenSettingsCommand { get; }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task ConfirmFilesAsync()
    {
        IsBusy.Value = true;
        StatusMessage.Value = Strings.StatusLoading;
        _metadataByFileId.Clear();

        try
        {
            var files = FileList.Files.ToList();
            var totalCount = files.Count;
            var completedCount = 0;
            var failedCount = 0;
            var inFlightNames = new ConcurrentDictionary<Guid, string>();
            BusyProgress.Value = new BusyProgressInfo(0, totalCount, []);

            // 各ファイルのメタデータ読み込み(ディスクI/O・PDF構造解析)は互いに独立しているため、
            // 上限付き並列実行で全体の待ち時間を短縮する。結果の反映(VMプロパティ更新・進捗更新)は
            // 呼び出し元スレッドで完了順に行い、スレッドセーフティを確保する。
            using var semaphore = new SemaphoreSlim(MaxParallelLoad);
            var loadTasks = files.Select(async file =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                inFlightNames[file.Id] = file.FileName;
                try
                {
                    var metadata = await _metadataService.ReadMetadataAsync(file.Model).ConfigureAwait(false);
                    return (File: file, Metadata: (PdfFileMetadata?)metadata, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (File: file, Metadata: (PdfFileMetadata?)null, Error: (Exception?)ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await foreach (var completedTask in Task.WhenEach(loadTasks))
            {
                var (file, metadata, error) = await completedTask;
                inFlightNames.TryRemove(file.Id, out _);
                completedCount++;

                if (error is not null)
                {
                    failedCount++;
                    file.MarkLoadFailed();
                    _logger.LogError(error, "PDFメタデータの読み込みに失敗しました: {File}", file.FilePath);
                    _dialogService.ShowError(Strings.LoadErrorDialogTitle, string.Format(Strings.LoadErrorMessageFormat, file.FileName, error.Message));
                }
                else
                {
                    file.ApplyPageCount(metadata!.PageCount);
                    _metadataByFileId[file.Id] = metadata;
                }

                BusyProgress.Value = new BusyProgressInfo(completedCount, totalCount, inFlightNames.Values.ToList());
            }

            var orderedFiles = files.Where(f => _metadataByFileId.ContainsKey(f.Id)).Select(f => f.Model).ToList();

            if (orderedFiles.Count == 0)
            {
                StatusMessage.Value = Strings.StatusNoLoadableFiles;
                return;
            }

            var effectiveBookmarks = MissingBookmarkFallback.ResolveEffectiveBookmarks(orderedFiles, _metadataByFileId);
            var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks(orderedFiles, effectiveBookmarks, _metadataByFileId);
            var fileNames = orderedFiles.ToDictionary(f => f.Id, f => f.FileName);
            var orderedFileIds = orderedFiles.Select(f => f.Id).ToList();

            await BookmarkTree.LoadAsync(merged, fileNames, orderedFileIds);
            Step.Value = WorkflowStep.EditBookmarks;

            StatusMessage.Value = failedCount == 0
                ? string.Format(Strings.StatusLoadedAllSucceededFormat, orderedFiles.Count)
                : string.Format(Strings.StatusLoadedWithFailuresFormat, orderedFiles.Count, failedCount);
        }
        finally
        {
            IsBusy.Value = false;
            BusyProgress.Value = null;
        }
    }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。
    /// 結合してPDFを保存するだけで手順を終える(リンク編集画面へは進まない)。</summary>
    internal async Task MergeAsync() => await MergeCoreAsync(continueToLinkEditing: false);

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。
    /// 結合完了後、続けてこの出力先ファイルへリンクを設定するリンク編集画面へ遷移する。</summary>
    internal async Task MergeAndEditLinksAsync() => await MergeCoreAsync(continueToLinkEditing: true);

    private async Task MergeCoreAsync(bool continueToLinkEditing)
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
            : Strings.DefaultMergedFileName;
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
        StatusMessage.Value = Strings.StatusMerging;
        BusyProgress.Value = new BusyProgressInfo(0, mergeTargetFiles.Count, []);

        try
        {
            var request = new PdfMergeRequest
            {
                Files = mergeTargetFiles.Select(f => f.Model).ToList(),
                Bookmarks = BookmarkTree.ToModel(),
                Properties = properties,
                OutputPath = outputPath,
            };

            var progress = new Progress<MergeProgress>(p =>
                BusyProgress.Value = new BusyProgressInfo(p.CompletedFileCount, p.TotalFileCount, [p.CurrentFileName]));

            await _mergeService.MergeAsync(request, progress);

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                var updated = _userSettings.Current.Clone();
                updated.LastOutputDirectory = outputDirectory;
                await _userSettings.SaveAsync(updated);
            }

            StatusMessage.Value = string.Format(Strings.StatusMergeCompleteFormat, outputPath);

            if (continueToLinkEditing)
            {
                _dialogService.ShowInfo(Strings.MergeCompleteDialogTitle, string.Format(Strings.MergeAndEditLinksCompleteMessageFormat, outputPath));

                // LinkEditorは既に結合・しおり設定済みの実ファイルを対象に動く。
                await LinkEditor.LoadAsync(outputPath);
                Step.Value = WorkflowStep.EditLinks;
            }
            else
            {
                _dialogService.ShowInfo(Strings.MergeCompleteDialogTitle, string.Format(Strings.MergeCompleteMessageFormat, outputPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF結合に失敗しました: {OutputPath}", outputPath);
            _dialogService.ShowError(Strings.MergeErrorDialogTitle, string.Format(Strings.MergeErrorMessageFormat, ex.Message));
        }
        finally
        {
            IsBusy.Value = false;
            BusyProgress.Value = null;
        }
    }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task SaveBookmarkSettingsAsync()
    {
        var mergeTargetFiles = FileList.Files.Where(f => !f.LoadFailed.Value).ToList();
        var firstFile = mergeTargetFiles.FirstOrDefault();

        // 保存先の既定値: PDF結合保存時と同じ規則(1番目のファイルの格納フォルダ)で、
        // ファイル名の拡張子だけをxmlに変える。
        var suggestedFileName = firstFile is not null
            ? $"{Path.GetFileNameWithoutExtension(firstFile.FilePath)}_merged.xml"
            : Strings.DefaultBookmarkSettingsFileName;
        var initialDirectory = firstFile is not null
            ? Path.GetDirectoryName(firstFile.FilePath)
            : _userSettings.Current.LastOutputDirectory;

        var outputPath = await _dialogService.ShowSaveBookmarkSettingsDialogAsync(suggestedFileName, initialDirectory);
        if (outputPath is null)
        {
            return;
        }

        IsBusy.Value = true;
        StatusMessage.Value = Strings.StatusSavingBookmarkSettings;

        try
        {
            await _bookmarkSettingsExportService.ExportAsync(BookmarkTree.ToExportModel(), outputPath);

            StatusMessage.Value = string.Format(Strings.StatusSaveBookmarkSettingsCompleteFormat, outputPath);
            _dialogService.ShowInfo(Strings.SaveBookmarkSettingsCompleteDialogTitle, string.Format(Strings.SaveBookmarkSettingsCompleteMessageFormat, outputPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "しおり設定ファイルの保存に失敗しました: {OutputPath}", outputPath);
            _dialogService.ShowError(Strings.SaveBookmarkSettingsErrorDialogTitle, string.Format(Strings.SaveBookmarkSettingsErrorMessageFormat, ex.Message));
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task FinishLinkEditingAsync()
    {
        var filePath = LinkEditor.FilePath.Value;

        IsBusy.Value = true;
        StatusMessage.Value = Strings.StatusApplyingLinks;

        try
        {
            await LinkEditor.FinishAsync();

            StatusMessage.Value = string.Format(Strings.StatusApplyLinksCompleteFormat, filePath);
            _dialogService.ShowInfo(Strings.ApplyLinksCompleteDialogTitle, string.Format(Strings.ApplyLinksCompleteMessageFormat, filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リンクの反映に失敗しました: {FilePath}", filePath);
            _dialogService.ShowError(Strings.ApplyLinksErrorDialogTitle, string.Format(Strings.ApplyLinksErrorMessageFormat, ex.Message));
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    /// <summary>internal: PdfBookmarkMerger.App.Testsから直接呼び出して回帰テストするため。</summary>
    internal async Task OpenSettingsAsync()
    {
        var updated = await _dialogService.ShowSettingsDialogAsync(_userSettings.Current);
        if (updated is null)
        {
            return;
        }

        await _userSettings.SaveAsync(updated);

        // 「結合してリンク編集へ進む」ボタンの表示・非表示のみをリアルタイムに切り替える
        // (ThemeMode/Languageと異なり、テーマ再適用やウィンドウ再構築を伴わない単純な
        // バインディング先の値なので、ここで更新するだけでXAML側が即座に追従する)。
        ShowMergeAndEditLinksButton.Value = updated.ShowMergeAndEditLinksButton;
    }
}
