using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 文字選択によって確定した、まだジャンプ先が未設定のリンク候補。行ごとの矩形(PDFユーザー空間)を持つ
/// (複数行にまたがる選択は、行単位で複数の矩形として保持し、確定時に同一GroupIdの複数リンクになる)。
/// </summary>
public sealed record PendingLinkSelection(int SourcePageIndex, IReadOnlyList<PdfRect> LineRects);

/// <summary>
/// リンク一覧UI向けの、1件のリンク(GroupId単位)の要約情報。
/// </summary>
/// <param name="IsPreExisting">
/// PDFファイルに元から含まれていた(結合元ファイルに元々あった、または以前にこのアプリで保存した)
/// リンクかどうか。PdfLinkAnnotationServiceはModifyモードでの追記のみ行い既存の注釈を安全に
/// 削除できないため、この種のリンクは編集・削除の対象にできず、一覧では表示のみ・確認ジャンプのみ可能。
/// </param>
public sealed record LinkGroupInfo(Guid GroupId, int SourcePageIndex, int TargetPageIndex, int RectCount, bool IsPreExisting);

/// <summary>
/// リンク編集画面(手順4)を統括するViewModel。結合・しおり設定済みの単一PDFファイルを対象に、
/// ページのプレビュー描画・ページ送り・拡大縮小・しおり一覧からのジャンプ・文字選択によるリンク作成・
/// リンクの削除を扱う。
/// </summary>
public sealed class LinkEditorViewModel : ViewModelBase
{
    /// <summary>
    /// 選択範囲を行ごとに分割する際、隣接する文字のBottom座標(pt)がこれ以上離れていれば
    /// 別の行とみなす。通常のフォントサイズでの行間より十分小さく、ベースラインの微小なブレより
    /// 十分大きい値。
    /// </summary>
    private const double LineBreakToleranceInPoints = 2.0;

    private readonly IPdfPageRenderer _pageRenderer;
    private readonly IPdfTextExtractor _textExtractor;
    private readonly IPdfMetadataService _metadataService;
    private readonly IPdfLinkAnnotationService _linkAnnotationService;
    private readonly ILogger<LinkEditorViewModel> _logger;

    private CancellationTokenSource? _metadataCts;
    private int? _lastLoadedLettersPageIndex;
    private int? _selectionAnchorLetterIndex;
    private int? _selectionFocusLetterIndex;
    private PdfPageSlotViewModel? _currentSlot;

    /// <summary>先頭ページ(0ページ目)のPDFユーザー空間サイズ(pt)。連続スクロール表示で、
    /// まだ描画されていないページの領域を確保するプレースホルダのサイズ計算に使う
    /// (ページごとの実サイズを事前に全件取得すると大規模PDFで遅くなるため、先頭ページのサイズで代用する)。</summary>
    private (double Width, double Height) _placeholderPageSizeInPoints;

    /// <summary>ビューポートに入っている(=LoadPageSlotAsyncが呼ばれ、UnloadPageSlotがまだ呼ばれていない)ページ番号。
    /// ズーム変更時に再描画すべき対象を絞り込むために使う。</summary>
    private readonly HashSet<int> _visiblePageIndices = [];

    private readonly Dictionary<int, CancellationTokenSource> _slotRenderCts = [];

    /// <summary>
    /// LoadAsync直後(まだリンクを一切反映していない状態)の一時的な複製。FinishAsyncは毎回この状態を
    /// FilePathへ復元してからLinksを反映するため、「完了」を複数回押しても注釈が重複しない。
    /// </summary>
    private string? _pristineBackupPath;

    /// <summary>
    /// LoadAsyncでReadExistingLinksAsyncにより読み取った(=既にファイルに書き込み済みの)Linksの
    /// Id集合。FinishAsyncはこれらを除いた分だけをApplyLinksAsyncへ渡す
    /// (pristineBackupから復元した時点で既に含まれているため、そのまま渡すと重複してしまう)。
    /// </summary>
    private readonly HashSet<Guid> _preExistingLinkIds = [];

    public LinkEditorViewModel(
        IPdfPageRenderer pageRenderer,
        IPdfTextExtractor textExtractor,
        IPdfMetadataService metadataService,
        IPdfLinkAnnotationService linkAnnotationService,
        ILogger<LinkEditorViewModel> logger)
    {
        _pageRenderer = pageRenderer;
        _textExtractor = textExtractor;
        _metadataService = metadataService;
        _linkAnnotationService = linkAnnotationService;
        _logger = logger;

        Disposable.Create(DeletePristineBackup).AddTo(Disposables);

        FilePath = new ReactivePropertySlim<string?>(null).AddTo(Disposables);
        PageCount = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        CurrentPageIndex = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        PageNumberInput = new ReactivePropertySlim<int>(1).AddTo(Disposables);
        ZoomScale = new ReactivePropertySlim<float>(1.0f).AddTo(Disposables);
        PageSlots = [];
        PlaceholderWidth = new ReactivePropertySlim<double>(0).AddTo(Disposables);
        PlaceholderHeight = new ReactivePropertySlim<double>(0).AddTo(Disposables);
        IsBusy = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        Bookmarks = new ReactivePropertySlim<IReadOnlyList<BookmarkNode>>([]).AddTo(Disposables);
        Letters = new ReactivePropertySlim<IReadOnlyList<PdfTextLetter>>([]).AddTo(Disposables);
        PageHeight = new ReactivePropertySlim<double>(0).AddTo(Disposables);
        Links = [];
        LinkGroups = new ReactivePropertySlim<IReadOnlyList<LinkGroupInfo>>([]).AddTo(Disposables);
        Links.CollectionChanged += (_, _) => RecomputeLinkGroups();
        PendingSelection = new ReactivePropertySlim<PendingLinkSelection?>(null).AddTo(Disposables);
        IsPickingArbitraryTarget = new ReactivePropertySlim<bool>(false).AddTo(Disposables);

        var canGoPrevious = CurrentPageIndex.CombineLatest(IsBusy, (page, busy) => page > 0 && !busy);
        PreviousPageCommand = new ReactiveCommand(canGoPrevious).AddTo(Disposables);
        PreviousPageCommand.Subscribe(() => CurrentPageIndex.Value--).AddTo(Disposables);

        var canGoNext = CurrentPageIndex.CombineLatest(PageCount, IsBusy, (page, count, busy) => page < count - 1 && !busy);
        NextPageCommand = new ReactiveCommand(canGoNext).AddTo(Disposables);
        NextPageCommand.Subscribe(() => CurrentPageIndex.Value++).AddTo(Disposables);

        const float minZoom = 0.25f;
        const float maxZoom = 4.0f;
        var canZoomIn = ZoomScale.CombineLatest(IsBusy, (zoom, busy) => zoom < maxZoom && !busy);
        ZoomInCommand = new ReactiveCommand(canZoomIn).AddTo(Disposables);
        ZoomInCommand.Subscribe(() => ZoomScale.Value = Math.Min(maxZoom, ZoomScale.Value * 1.25f)).AddTo(Disposables);

        var canZoomOut = ZoomScale.CombineLatest(IsBusy, (zoom, busy) => zoom > minZoom && !busy);
        ZoomOutCommand = new ReactiveCommand(canZoomOut).AddTo(Disposables);
        ZoomOutCommand.Subscribe(() => ZoomScale.Value = Math.Max(minZoom, ZoomScale.Value / 1.25f)).AddTo(Disposables);

        JumpToPageCommand = new ReactiveCommand<int>(IsBusy.Select(b => !b)).AddTo(Disposables);
        JumpToPageCommand.Subscribe(pageIndex =>
        {
            if (pageIndex >= 0 && pageIndex < PageCount.Value)
            {
                CurrentPageIndex.Value = pageIndex;
            }
        }).AddTo(Disposables);

        CancelPendingSelectionCommand = new ReactiveCommand().AddTo(Disposables);
        CancelPendingSelectionCommand.Subscribe(CancelPendingSelection).AddTo(Disposables);

        BeginPickArbitraryTargetCommand = new ReactiveCommand(PendingSelection.Select(p => p is not null)).AddTo(Disposables);
        BeginPickArbitraryTargetCommand.Subscribe(() => IsPickingArbitraryTarget.Value = true).AddTo(Disposables);

        CreateLinkToBookmarkCommand = new ReactiveCommand<BookmarkNode>(PendingSelection.Select(p => p is not null)).AddTo(Disposables);
        CreateLinkToBookmarkCommand.Subscribe(CreateLinkToBookmark).AddTo(Disposables);

        DeleteLinkGroupCommand = new ReactiveCommand<Guid>().AddTo(Disposables);
        DeleteLinkGroupCommand.Subscribe(DeleteLinkGroup).AddTo(Disposables);

        EditLinkGroupCommand = new ReactiveCommand<Guid>().AddTo(Disposables);
        EditLinkGroupCommand.Subscribe(BeginEditLinkGroup).AddTo(Disposables);

        // 各コマンドのCanExecute(CombineLatest)をCurrentPageIndex/ZoomScaleへ先に購読させた後で、
        // 実際に副作用(現在ページのメタデータ取得・プレースホルダ再計算)をトリガーする購読を登録する。
        // 逆順にすると、処理がFakePdfPageRenderer等で同期的に完了する環境(=単体テスト)で、
        // 「IsBusyの変化(CombineLatestへ古いCurrentPageIndexの値のまま伝播)」が
        // 「CurrentPageIndexの変化そのもの(CombineLatestへの再伝播)」より先に処理されてしまい、
        // CanExecuteの最終値が古いページ番号を基準にした値のまま取り残されるレースが発生しうる
        // (実際にテストのflaky failureとして観測してから、この順序に修正した)。
        CurrentPageIndex.Subscribe(OnCurrentPageIndexChanged).AddTo(Disposables);
        ZoomScale.Subscribe(_ => TriggerZoomChanged()).AddTo(Disposables);
        PageNumberInput.Subscribe(OnPageNumberInputChanged).AddTo(Disposables);
    }

    public ReactivePropertySlim<string?> FilePath { get; }

    public ReactivePropertySlim<int> PageCount { get; }

    public ReactivePropertySlim<int> CurrentPageIndex { get; }

    /// <summary>ページ送りツールバーのテキストボックス向け、1始まりの現在ページ番号。
    /// CurrentPageIndexと双方向に同期する(範囲外の入力は最も近い有効な値へ丸める)。</summary>
    public ReactivePropertySlim<int> PageNumberInput { get; }

    /// <summary>現在ページの高さ(pt)。PdfCoordinateMapperでのピクセル座標変換に使う。</summary>
    public ReactivePropertySlim<double> PageHeight { get; }

    /// <summary>現在ページの文字(グリフ)一覧。文字選択のヒットテストに使う。</summary>
    public ReactivePropertySlim<IReadOnlyList<PdfTextLetter>> Letters { get; }

    /// <summary>これまでに作成した全リンク(全ページ分)。</summary>
    public ObservableCollection<LinkAnnotationNode> Links { get; }

    /// <summary>リンク一覧UI向けの要約情報。GroupIdごとに1件、Linksの変化のたびに再計算される。</summary>
    public ReactivePropertySlim<IReadOnlyList<LinkGroupInfo>> LinkGroups { get; }

    /// <summary>
    /// 文字選択が確定し、ジャンプ先の指定待ちになっているリンク候補。nullの間は選択操作前・
    /// リンク確定後の状態。
    /// </summary>
    public ReactivePropertySlim<PendingLinkSelection?> PendingSelection { get; }

    /// <summary>trueの間、プレビュー上のクリックは文字選択ではなく「任意のジャンプ先位置の指定」として扱う。</summary>
    public ReactivePropertySlim<bool> IsPickingArbitraryTarget { get; }

    public ReactivePropertySlim<float> ZoomScale { get; }

    /// <summary>
    /// 全ページ分のプレースホルダ。連続スクロール表示のItemsSourceとして使い、各要素の画像は
    /// ビューポートに入った時にLoadPageSlotAsyncで遅延描画し、外れた時にUnloadPageSlotで破棄する
    /// (数千ページ規模のPDFでも全ページの画像を同時に保持しないため)。
    /// </summary>
    public ObservableCollection<PdfPageSlotViewModel> PageSlots { get; }

    /// <summary>
    /// まだ描画されていないページの領域確保に使う、現在のズーム倍率でのプレースホルダの幅・高さ(px)。
    /// 先頭ページのサイズを全ページで代用する(ページごとの実サイズ取得は大規模PDFで高コストなため)。
    /// </summary>
    public ReactivePropertySlim<double> PlaceholderWidth { get; }

    public ReactivePropertySlim<double> PlaceholderHeight { get; }

    public ReactivePropertySlim<bool> IsBusy { get; }

    public ReactivePropertySlim<IReadOnlyList<BookmarkNode>> Bookmarks { get; }

    public ReactiveCommand PreviousPageCommand { get; }

    public ReactiveCommand NextPageCommand { get; }

    public ReactiveCommand ZoomInCommand { get; }

    public ReactiveCommand ZoomOutCommand { get; }

    public ReactiveCommand<int> JumpToPageCommand { get; }

    public ReactiveCommand CancelPendingSelectionCommand { get; }

    public ReactiveCommand BeginPickArbitraryTargetCommand { get; }

    public ReactiveCommand<BookmarkNode> CreateLinkToBookmarkCommand { get; }

    public ReactiveCommand<Guid> DeleteLinkGroupCommand { get; }

    public ReactiveCommand<Guid> EditLinkGroupCommand { get; }

    /// <summary>
    /// 結合・しおり設定済みの<paramref name="filePath"/>を読み込む。ページ数・しおり一覧を取得し、
    /// 全ページ分のプレースホルダ(PageSlots)を用意する。各ページの実際の描画は、連続スクロール表示で
    /// そのページがビューポートに入った時にLoadPageSlotAsyncが呼ばれてから行われる。
    /// </summary>
    public async Task LoadAsync(string filePath, CancellationToken ct = default)
    {
        IsBusy.Value = true;
        try
        {
            var metadata = await _metadataService.ReadMetadataAsync(new PdfFileEntry { FilePath = filePath }, ct);
            var (placeholderWidth, placeholderHeight) = metadata.PageCount > 0
                ? await _pageRenderer.GetPageSizeAsync(filePath, 0, ct)
                : (0.0, 0.0);

            FilePath.Value = filePath;
            PageCount.Value = metadata.PageCount;
            Bookmarks.Value = metadata.Bookmarks;
            ZoomScale.Value = 1.0f;
            Links.Clear();
            _preExistingLinkIds.Clear();
            _lastLoadedLettersPageIndex = null;
            _visiblePageIndices.Clear();
            CancelAllSlotRenders();
            CancelPendingSelection();

            _placeholderPageSizeInPoints = (placeholderWidth, placeholderHeight);
            RecomputePlaceholderSize();

            PageSlots.Clear();
            for (var i = 0; i < metadata.PageCount; i++)
            {
                PageSlots.Add(new PdfPageSlotViewModel(i));
            }

            _currentSlot = null;

            // ファイルが実在する場合のみバックアップを作り、既存のリンクを読み取る(単体テストでは
            // フィクションのパスを渡すことがあるため、存在しない場合はバックアップなし=FinishAsyncが
            // 安全にno-opするだけに留め、既存リンクの読み取りもスキップする)。
            DeletePristineBackup();
            if (File.Exists(filePath))
            {
                _pristineBackupPath = Path.Combine(Path.GetTempPath(), $"pdfbookmarkmerger-prelinks-{Guid.NewGuid():N}.pdf");
                File.Copy(filePath, _pristineBackupPath, overwrite: true);

                var existingLinks = await _linkAnnotationService.ReadExistingLinksAsync(filePath, ct);
                foreach (var link in existingLinks)
                {
                    _preExistingLinkIds.Add(link.Id);
                    Links.Add(link);
                }
            }

            // CurrentPageIndexがすでに0の場合はSubscribeが発火しないため、
            // OnCurrentPageIndexChangedが行う処理を明示的に呼び出す。
            if (CurrentPageIndex.Value == 0)
            {
                SyncCurrentSlotAndPageNumber(0);
                await LoadCurrentPageMetadataAsync(ct);
            }
            else
            {
                CurrentPageIndex.Value = 0;
            }
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    /// <summary>
    /// リンク編集を完了し、Linksの内容を出力ファイルへ反映する。FilePathを、LoadAsync直後(リンク未反映)の
    /// 状態を保持したバックアップから復元してからApplyLinksAsyncを呼ぶため、「完了」を複数回実行しても
    /// (その都度Linksの内容が変わっていても)注釈が重複することはない。
    /// Linksのうち、LoadAsync時に読み取った既存リンク(_preExistingLinkIds)は、復元したバックアップに
    /// 既に含まれているため、ApplyLinksAsyncには渡さない(渡すと二重に書き込まれてしまう)。
    /// </summary>
    public async Task FinishAsync(CancellationToken ct = default)
    {
        if (FilePath.Value is not { } filePath || _pristineBackupPath is null)
        {
            return;
        }

        IsBusy.Value = true;
        try
        {
            File.Copy(_pristineBackupPath, filePath, overwrite: true);
            var newLinks = Links.Where(l => !_preExistingLinkIds.Contains(l.Id)).ToList();
            await _linkAnnotationService.ApplyLinksAsync(filePath, newLinks, ct);
        }
        finally
        {
            IsBusy.Value = false;
        }
    }

    private void DeletePristineBackup()
    {
        if (_pristineBackupPath is not null && File.Exists(_pristineBackupPath))
        {
            try
            {
                File.Delete(_pristineBackupPath);
            }
            catch (IOException)
            {
                // ベストエフォート。一時フォルダはいずれOSが回収する。
            }
        }

        _pristineBackupPath = null;
    }

    /// <summary>
    /// CurrentPageIndexの変更を受けて、範囲を自己修正しつつPageNumberInputを同期し、
    /// IsCurrentの付け替え・現在ページのメタデータ取得をfire-and-forgetでトリガーする。
    /// </summary>
    private void OnCurrentPageIndexChanged(int pageIndex)
    {
        if (PageCount.Value > 0 && (pageIndex < 0 || pageIndex >= PageCount.Value))
        {
            // 範囲外の値が設定された場合(不正なJumpToPage呼び出し等)は最も近い有効な値へ丸める。
            // 再入するが、ReactivePropertySlimは値が変化しない限り再通知しないため収束する。
            CurrentPageIndex.Value = Math.Clamp(pageIndex, 0, PageCount.Value - 1);
            return;
        }

        SyncCurrentSlotAndPageNumber(pageIndex);
        TriggerLoadCurrentPageMetadata();
    }

    /// <summary>現在ページのIsCurrentフラグの付け替え・PageNumberInputの同期のみを行う
    /// (メタデータ取得は伴わない、OnCurrentPageIndexChangedから切り出した同期処理)。</summary>
    private void SyncCurrentSlotAndPageNumber(int pageIndex)
    {
        if (_currentSlot is not null)
        {
            _currentSlot.IsCurrent.Value = false;
        }

        _currentSlot = pageIndex >= 0 && pageIndex < PageSlots.Count ? PageSlots[pageIndex] : null;
        if (_currentSlot is not null)
        {
            _currentSlot.IsCurrent.Value = true;
        }

        PageNumberInput.Value = pageIndex + 1;
    }

    /// <summary>
    /// ページ送りツールバーのテキストボックス(1始まり)の変更を、範囲を丸めつつCurrentPageIndexへ反映する。
    /// </summary>
    private void OnPageNumberInputChanged(int pageNumber)
    {
        if (PageCount.Value <= 0)
        {
            return;
        }

        var clamped = Math.Clamp(pageNumber, 1, PageCount.Value);
        if (clamped != pageNumber)
        {
            PageNumberInput.Value = clamped;
            return;
        }

        CurrentPageIndex.Value = clamped - 1;
    }

    /// <summary>CurrentPageIndexの変更をfire-and-forgetでメタデータ取得に反映する
    /// (BookmarkTreeViewModel.TriggerRecomputeと同じラッパーパターン)。</summary>
    private async void TriggerLoadCurrentPageMetadata() => await LoadCurrentPageMetadataAsync(CancellationToken.None);

    /// <summary>
    /// 現在ページの高さ・(ページが変わった場合のみ)文字一覧を取得する。ページのビットマップ自体は
    /// PageSlots/LoadPageSlotAsyncが担当するため、ここでは扱わない(選択・オーバーレイの座標変換に
    /// 必要な、PDFユーザー空間のメタデータのみを取得する軽量な処理)。
    /// </summary>
    private async Task LoadCurrentPageMetadataAsync(CancellationToken ct)
    {
        var filePath = FilePath.Value;
        if (filePath is null)
        {
            return;
        }

        // ページ送りが連続操作された場合、古い取得要求を打ち切って最新のものだけを反映する。
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _metadataCts = cts;

        var pageIndex = CurrentPageIndex.Value;

        IsBusy.Value = true;
        try
        {
            var (_, height) = await _pageRenderer.GetPageSizeAsync(filePath, pageIndex, cts.Token);

            IReadOnlyList<PdfTextLetter>? letters = null;
            if (_lastLoadedLettersPageIndex != pageIndex)
            {
                letters = await _textExtractor.ExtractLettersAsync(filePath, pageIndex, cts.Token);
            }

            if (!cts.IsCancellationRequested)
            {
                PageHeight.Value = height;
                if (letters is not null)
                {
                    Letters.Value = letters;
                    _lastLoadedLettersPageIndex = pageIndex;
                    CancelPendingSelection();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 新しい取得要求に置き換えられただけなので無視する。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページ情報の取得に失敗しました: {FilePath} (page {PageIndex})", filePath, pageIndex);
        }
        finally
        {
            if (ReferenceEquals(_metadataCts, cts))
            {
                IsBusy.Value = false;
            }
        }
    }

    /// <summary>
    /// ズーム変更をfire-and-forgetでプレースホルダサイズ再計算・表示中ページの再描画に反映する。
    /// </summary>
    private async void TriggerZoomChanged() => await HandleZoomChangedAsync();

    private Task HandleZoomChangedAsync()
    {
        RecomputePlaceholderSize();

        // 描画済みの画像は古い倍率のものなので、いったん全て破棄する
        // (ビューポート外のものは元々null、破棄済みのものへの再設定は無害)。
        foreach (var slot in PageSlots)
        {
            slot.Image.Value = null;
        }

        // 現在ビューポートに入っているページだけを新しい倍率で再描画する
        // (数千ページ規模のPDFで全ページを一括再描画しないため)。
        var visibleSnapshot = _visiblePageIndices.ToList();
        var loadTasks = visibleSnapshot.Select(LoadPageSlotAsync);
        return Task.WhenAll(loadTasks);
    }

    private void RecomputePlaceholderSize()
    {
        var pixelsPerPoint = PdfCoordinateMapper.PixelsPerPoint(ZoomScale.Value);
        PlaceholderWidth.Value = _placeholderPageSizeInPoints.Width * pixelsPerPoint;
        PlaceholderHeight.Value = _placeholderPageSizeInPoints.Height * pixelsPerPoint;
    }

    /// <summary>
    /// 連続スクロール表示で<paramref name="pageIndex"/>のコンテナがビューポートに入った時に呼び出す。
    /// 未描画であれば描画し、描画済み・描画中であれば何もしない(コンテナのリサイクルによる
    /// 重複呼び出しを許容する)。
    /// </summary>
    public async Task LoadPageSlotAsync(int pageIndex)
    {
        if (FilePath.Value is not { } filePath || pageIndex < 0 || pageIndex >= PageSlots.Count)
        {
            return;
        }

        _visiblePageIndices.Add(pageIndex);

        var slot = PageSlots[pageIndex];
        if (slot.Image.Value is not null)
        {
            return;
        }

        if (_slotRenderCts.TryGetValue(pageIndex, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _slotRenderCts[pageIndex] = cts;

        try
        {
            var image = await _pageRenderer.RenderPageAsync(filePath, pageIndex, ZoomScale.Value, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                slot.Image.Value = image;
            }
        }
        catch (OperationCanceledException)
        {
            // ビューポート外へスクロールされ、UnloadPageSlotで打ち切られただけなので無視する。
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ページのプレビュー描画に失敗しました: {FilePath} (page {PageIndex})", filePath, pageIndex);
        }
        finally
        {
            if (_slotRenderCts.TryGetValue(pageIndex, out var currentCts) && ReferenceEquals(currentCts, cts))
            {
                _slotRenderCts.Remove(pageIndex);
            }
        }
    }

    /// <summary>
    /// <paramref name="pageIndex"/>のコンテナがビューポートから外れた時に呼び出す。描画中であれば打ち切り、
    /// 保持していた画像を破棄してメモリを解放する(数千ページ規模のPDFでも全ページ分のビットマップを
    /// 同時に保持しないための、連続スクロール表示の要)。
    /// </summary>
    public void UnloadPageSlot(int pageIndex)
    {
        _visiblePageIndices.Remove(pageIndex);

        if (_slotRenderCts.TryGetValue(pageIndex, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _slotRenderCts.Remove(pageIndex);
        }

        if (pageIndex >= 0 && pageIndex < PageSlots.Count)
        {
            PageSlots[pageIndex].Image.Value = null;
        }
    }

    private void CancelAllSlotRenders()
    {
        foreach (var cts in _slotRenderCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _slotRenderCts.Clear();
    }

    /// <summary>プレビュー上のドラッグ開始位置(PDFユーザー空間座標)から、文字単位の選択を開始する。</summary>
    public void BeginTextSelection(double pdfX, double pdfY)
    {
        var index = FindNearestLetterIndex(Letters.Value, pdfX, pdfY);
        _selectionAnchorLetterIndex = index;
        _selectionFocusLetterIndex = index;
        PendingSelection.Value = null;
    }

    /// <summary>ドラッグ中の現在位置(PDFユーザー空間座標)まで選択範囲を伸縮する。</summary>
    public void UpdateTextSelection(double pdfX, double pdfY)
    {
        if (_selectionAnchorLetterIndex is null)
        {
            return;
        }

        _selectionFocusLetterIndex = FindNearestLetterIndex(Letters.Value, pdfX, pdfY);
    }

    /// <summary>ドラッグを終了し、選択範囲を行ごとの矩形へ確定してPendingSelectionへ反映する。</summary>
    public void EndTextSelection()
    {
        if (_selectionAnchorLetterIndex is not { } anchor || _selectionFocusLetterIndex is not { } focus)
        {
            _selectionAnchorLetterIndex = null;
            _selectionFocusLetterIndex = null;
            return;
        }

        _selectionAnchorLetterIndex = null;
        _selectionFocusLetterIndex = null;

        var start = Math.Min(anchor, focus);
        var end = Math.Max(anchor, focus);
        var lineRects = GroupLettersIntoLineRects(Letters.Value, start, end);
        if (lineRects.Count > 0)
        {
            PendingSelection.Value = new PendingLinkSelection(CurrentPageIndex.Value, lineRects);
        }
    }

    /// <summary>選択中・確定待ちのリンク候補を破棄する。</summary>
    public void CancelPendingSelection()
    {
        _selectionAnchorLetterIndex = null;
        _selectionFocusLetterIndex = null;
        PendingSelection.Value = null;
        IsPickingArbitraryTarget.Value = false;
    }

    /// <summary>
    /// PendingSelectionのジャンプ先として<paramref name="bookmark"/>を選び、リンクを確定する
    /// (しおりのDestinationType・座標をそのままコピーする)。
    /// </summary>
    public void CreateLinkToBookmark(BookmarkNode bookmark)
    {
        if (PendingSelection.Value is not { } pending)
        {
            return;
        }

        AddLinks(pending, bookmark.OriginalPageIndex, bookmark.DestinationType, bookmark.Left, bookmark.Top, bookmark.Right, bookmark.Bottom, bookmark.Zoom);
        PendingSelection.Value = null;
        IsPickingArbitraryTarget.Value = false;
    }

    /// <summary>
    /// IsPickingArbitraryTarget中に呼び出す。プレビュー上でクリックされた位置(PDFユーザー空間座標)を
    /// XYZ形式のジャンプ先として、PendingSelectionのリンクを確定する。
    /// </summary>
    public void PickArbitraryTargetAndCreateLink(int targetPageIndex, double pdfX, double pdfY)
    {
        if (!IsPickingArbitraryTarget.Value || PendingSelection.Value is not { } pending)
        {
            return;
        }

        AddLinks(pending, targetPageIndex, BookmarkDestinationType.XYZ, pdfX, pdfY, null, null, null);
        PendingSelection.Value = null;
        IsPickingArbitraryTarget.Value = false;
    }

    private void AddLinks(
        PendingLinkSelection pending,
        int targetPageIndex,
        BookmarkDestinationType destinationType,
        double? left,
        double? top,
        double? right,
        double? bottom,
        double? zoom)
    {
        var groupId = Guid.NewGuid();
        foreach (var rect in pending.LineRects)
        {
            Links.Add(new LinkAnnotationNode
            {
                GroupId = groupId,
                SourcePageIndex = pending.SourcePageIndex,
                SourceRect = rect,
                TargetPageIndex = targetPageIndex,
                DestinationType = destinationType,
                Left = left,
                Top = top,
                Right = right,
                Bottom = bottom,
                Zoom = zoom,
            });
        }
    }

    /// <summary>指定GroupIdに属する全リンク(複数行選択から生成された一連のリンク)をまとめて削除する。</summary>
    public void DeleteLinkGroup(Guid groupId)
    {
        var toRemove = Links.Where(l => l.GroupId == groupId).ToList();
        if (toRemove.Count == 0 || toRemove.Any(l => _preExistingLinkIds.Contains(l.Id)))
        {
            // PDFに既に含まれているリンクは、PdfLinkAnnotationServiceがModifyモードでの追記のみを
            // 行い既存の注釈を安全に削除できないため、この画面からは削除できない
            // (UI側もLinkGroupInfo.IsPreExistingを見て削除ボタンを表示しない)。
            return;
        }

        foreach (var link in toRemove)
        {
            Links.Remove(link);
        }
    }

    /// <summary>
    /// 指定GroupIdのリンクのジャンプ先を編集する。既存のリンクをいったん削除し、同じホットスポット
    /// (SourceRect群)をPendingSelectionへ復元する。これにより、CreateLinkToBookmark/
    /// PickArbitraryTargetAndCreateLinkをそのまま使って新しいジャンプ先を選び直せる
    /// (確定後は新しいGroupIdが振られる。GroupId自体は内部的な集約用の値でしかないため、
    /// 編集の前後で同一である必要はない)。
    /// </summary>
    public void BeginEditLinkGroup(Guid groupId)
    {
        var existing = Links.Where(l => l.GroupId == groupId).ToList();
        if (existing.Count == 0 || existing.Any(l => _preExistingLinkIds.Contains(l.Id)))
        {
            // PDFに既に含まれているリンクは、削除と同じ理由でジャンプ先を編集できない
            // (UI側もLinkGroupInfo.IsPreExistingを見て編集ボタンを表示しない)。
            return;
        }

        foreach (var link in existing)
        {
            Links.Remove(link);
        }

        var sourcePageIndex = existing[0].SourcePageIndex;
        var lineRects = existing.Select(l => l.SourceRect).ToList();
        PendingSelection.Value = new PendingLinkSelection(sourcePageIndex, lineRects);
    }

    private void RecomputeLinkGroups()
    {
        LinkGroups.Value = Links
            .GroupBy(l => l.GroupId)
            .Select(g => new LinkGroupInfo(
                g.Key,
                g.First().SourcePageIndex,
                g.First().TargetPageIndex,
                g.Count(),
                IsPreExisting: g.All(l => _preExistingLinkIds.Contains(l.Id))))
            .ToList();
    }

    /// <summary>
    /// (pdfX, pdfY)に矩形が重なる文字を探す。無ければ、中心点までの距離が最も近い文字を返す
    /// (ドラッグがわずかに文字の外側へ外れても選択が破綻しないようにするため)。
    /// </summary>
    private static int? FindNearestLetterIndex(IReadOnlyList<PdfTextLetter> letters, double x, double y)
    {
        if (letters.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < letters.Count; i++)
        {
            var r = letters[i].Rect;
            if (x >= r.Left && x <= r.Right && y >= r.Bottom && y <= r.Top)
            {
                return i;
            }
        }

        var bestIndex = 0;
        var bestDistanceSquared = double.MaxValue;
        for (var i = 0; i < letters.Count; i++)
        {
            var r = letters[i].Rect;
            var cx = (r.Left + r.Right) / 2;
            var cy = (r.Bottom + r.Top) / 2;
            var dx = x - cx;
            var dy = y - cy;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// [start, end]範囲の文字を、行(隣接文字のBottom座標がLineBreakToleranceInPoints以上離れていれば
    /// 改行とみなす)ごとにグループ化し、各行の外接矩形を返す。
    /// </summary>
    private static List<PdfRect> GroupLettersIntoLineRects(IReadOnlyList<PdfTextLetter> letters, int start, int end)
    {
        var rects = new List<PdfRect>();
        double? previousBottom = null;
        double left = 0, bottom = 0, right = 0, top = 0;
        var hasCurrentLine = false;

        for (var i = start; i <= end; i++)
        {
            var r = letters[i].Rect;
            var isNewLine = previousBottom is null || Math.Abs(r.Bottom - previousBottom.Value) > LineBreakToleranceInPoints;

            if (isNewLine)
            {
                if (hasCurrentLine)
                {
                    rects.Add(new PdfRect(Left: left, Bottom: bottom, Right: right, Top: top));
                }

                left = r.Left;
                bottom = r.Bottom;
                right = r.Right;
                top = r.Top;
                hasCurrentLine = true;
            }
            else
            {
                left = Math.Min(left, r.Left);
                bottom = Math.Min(bottom, r.Bottom);
                right = Math.Max(right, r.Right);
                top = Math.Max(top, r.Top);
            }

            previousBottom = r.Bottom;
        }

        if (hasCurrentLine)
        {
            rects.Add(new PdfRect(Left: left, Bottom: bottom, Right: right, Top: top));
        }

        return rects;
    }
}
