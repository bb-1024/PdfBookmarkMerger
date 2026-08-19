using System.Collections.ObjectModel;
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

/// <summary>リンク一覧UI向けの、1件のリンク(GroupId単位)の要約情報。</summary>
public sealed record LinkGroupInfo(Guid GroupId, int SourcePageIndex, int TargetPageIndex, int RectCount);

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
    private readonly ILogger<LinkEditorViewModel> _logger;

    private CancellationTokenSource? _renderCts;
    private int? _lastLoadedLettersPageIndex;
    private int? _selectionAnchorLetterIndex;
    private int? _selectionFocusLetterIndex;

    public LinkEditorViewModel(
        IPdfPageRenderer pageRenderer,
        IPdfTextExtractor textExtractor,
        IPdfMetadataService metadataService,
        ILogger<LinkEditorViewModel> logger)
    {
        _pageRenderer = pageRenderer;
        _textExtractor = textExtractor;
        _metadataService = metadataService;
        _logger = logger;

        FilePath = new ReactivePropertySlim<string?>(null).AddTo(Disposables);
        PageCount = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        CurrentPageIndex = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        ZoomScale = new ReactivePropertySlim<float>(1.0f).AddTo(Disposables);
        PageImage = new ReactivePropertySlim<byte[]?>(null).AddTo(Disposables);
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
        // 実際にページ描画をトリガーする副作用の購読を登録する。逆順にすると、
        // 描画がFakePdfPageRenderer等で同期的に完了する環境(=単体テスト)で、
        // 「IsBusyの変化(CombineLatestへ古いCurrentPageIndexの値のまま伝播)」が
        // 「CurrentPageIndexの変化そのもの(CombineLatestへの再伝播)」より先に処理されてしまい、
        // CanExecuteの最終値が古いページ番号を基準にした値のまま取り残されるレースが発生しうる
        // (実際にテストのflaky failureとして観測してから、この順序に修正した)。
        CurrentPageIndex.Subscribe(_ => TriggerRenderCurrentPage()).AddTo(Disposables);
        ZoomScale.Subscribe(_ => TriggerRenderCurrentPage()).AddTo(Disposables);
    }

    public ReactivePropertySlim<string?> FilePath { get; }

    public ReactivePropertySlim<int> PageCount { get; }

    public ReactivePropertySlim<int> CurrentPageIndex { get; }

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

    public ReactivePropertySlim<byte[]?> PageImage { get; }

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
    /// 1ページ目を描画する。
    /// </summary>
    public async Task LoadAsync(string filePath, CancellationToken ct = default)
    {
        IsBusy.Value = true;
        try
        {
            var metadata = await _metadataService.ReadMetadataAsync(new PdfFileEntry { FilePath = filePath }, ct).ConfigureAwait(false);

            FilePath.Value = filePath;
            PageCount.Value = metadata.PageCount;
            Bookmarks.Value = metadata.Bookmarks;
            ZoomScale.Value = 1.0f;
            Links.Clear();
            _lastLoadedLettersPageIndex = null;
            CancelPendingSelection();

            // CurrentPageIndexがすでに0の場合はSubscribeが発火しないため、明示的に描画をトリガーする。
            if (CurrentPageIndex.Value == 0)
            {
                await RenderCurrentPageAsync(ct).ConfigureAwait(false);
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
    /// CurrentPageIndex/ZoomScaleの変更をfire-and-forgetで描画に反映する
    /// (BookmarkTreeViewModel.TriggerRecomputeと同じラッパーパターン)。
    /// </summary>
    private async void TriggerRenderCurrentPage() => await RenderCurrentPageAsync(CancellationToken.None);

    /// <summary>
    /// 現在ページのビットマップ・サイズ・(ページが変わった場合のみ)文字一覧を描画・取得する。
    /// ページ描画と文字抽出を同じCancellationTokenSource・同じIsBusyトグルの下で直列に行うことで、
    /// 「CurrentPageIndexの変更で2つの独立したfire-and-foroundチェーンが競合し、片方のIsBusy解除が
    /// もう片方の完了より早く走ってCanExecuteが不安定になる」というレースを避けている
    /// (この設計は実際にテストのflaky failureとして観測してから導入した)。
    /// ズームのみの変更では文字位置は変わらないため、ページが変わっていない場合は文字抽出をスキップする。
    /// </summary>
    private async Task RenderCurrentPageAsync(CancellationToken ct)
    {
        var filePath = FilePath.Value;
        if (filePath is null)
        {
            return;
        }

        // ページ送り・ズームが連続操作された場合、古い描画要求を打ち切って最新のものだけを反映する。
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _renderCts = cts;

        var pageIndex = CurrentPageIndex.Value;

        IsBusy.Value = true;
        try
        {
            var image = await _pageRenderer.RenderPageAsync(filePath, pageIndex, ZoomScale.Value, cts.Token).ConfigureAwait(false);
            var (_, height) = await _pageRenderer.GetPageSizeAsync(filePath, pageIndex, cts.Token).ConfigureAwait(false);

            IReadOnlyList<PdfTextLetter>? letters = null;
            if (_lastLoadedLettersPageIndex != pageIndex)
            {
                letters = await _textExtractor.ExtractLettersAsync(filePath, pageIndex, cts.Token).ConfigureAwait(false);
            }

            if (!cts.IsCancellationRequested)
            {
                PageImage.Value = image;
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
            // 新しい描画要求に置き換えられただけなので無視する。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページの描画に失敗しました: {FilePath} (page {PageIndex})", filePath, pageIndex);
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts))
            {
                IsBusy.Value = false;
            }
        }
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
        if (existing.Count == 0)
        {
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
            .Select(g => new LinkGroupInfo(g.Key, g.First().SourcePageIndex, g.First().TargetPageIndex, g.Count()))
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
