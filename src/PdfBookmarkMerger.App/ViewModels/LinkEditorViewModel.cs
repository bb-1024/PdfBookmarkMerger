using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// リンク編集画面(手順4)を統括するViewModel。結合・しおり設定済みの単一PDFファイルを対象に、
/// ページのプレビュー描画・ページ送り・拡大縮小・しおり一覧からのジャンプを扱う。
/// このフェーズではリンクの作成・編集・削除はまだ実装しない(骨格のみ)。
/// </summary>
public sealed class LinkEditorViewModel : ViewModelBase
{
    private readonly IPdfPageRenderer _pageRenderer;
    private readonly IPdfMetadataService _metadataService;
    private readonly ILogger<LinkEditorViewModel> _logger;

    private CancellationTokenSource? _renderCts;

    public LinkEditorViewModel(
        IPdfPageRenderer pageRenderer,
        IPdfMetadataService metadataService,
        ILogger<LinkEditorViewModel> logger)
    {
        _pageRenderer = pageRenderer;
        _metadataService = metadataService;
        _logger = logger;

        FilePath = new ReactivePropertySlim<string?>(null).AddTo(Disposables);
        PageCount = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        CurrentPageIndex = new ReactivePropertySlim<int>(0).AddTo(Disposables);
        ZoomScale = new ReactivePropertySlim<float>(1.0f).AddTo(Disposables);
        PageImage = new ReactivePropertySlim<byte[]?>(null).AddTo(Disposables);
        IsBusy = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
        Bookmarks = new ReactivePropertySlim<IReadOnlyList<BookmarkNode>>([]).AddTo(Disposables);

        CurrentPageIndex.Subscribe(_ => TriggerRenderCurrentPage()).AddTo(Disposables);
        ZoomScale.Subscribe(_ => TriggerRenderCurrentPage()).AddTo(Disposables);

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
    }

    public ReactivePropertySlim<string?> FilePath { get; }

    public ReactivePropertySlim<int> PageCount { get; }

    public ReactivePropertySlim<int> CurrentPageIndex { get; }

    public ReactivePropertySlim<float> ZoomScale { get; }

    public ReactivePropertySlim<byte[]?> PageImage { get; }

    public ReactivePropertySlim<bool> IsBusy { get; }

    public ReactivePropertySlim<IReadOnlyList<BookmarkNode>> Bookmarks { get; }

    public ReactiveCommand PreviousPageCommand { get; }

    public ReactiveCommand NextPageCommand { get; }

    public ReactiveCommand ZoomInCommand { get; }

    public ReactiveCommand ZoomOutCommand { get; }

    public ReactiveCommand<int> JumpToPageCommand { get; }

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

        IsBusy.Value = true;
        try
        {
            var image = await _pageRenderer.RenderPageAsync(filePath, CurrentPageIndex.Value, ZoomScale.Value, cts.Token).ConfigureAwait(false);
            if (!cts.IsCancellationRequested)
            {
                PageImage.Value = image;
            }
        }
        catch (OperationCanceledException)
        {
            // 新しい描画要求に置き換えられただけなので無視する。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ページの描画に失敗しました: {FilePath} (page {PageIndex})", filePath, CurrentPageIndex.Value);
        }
        finally
        {
            if (ReferenceEquals(_renderCts, cts))
            {
                IsBusy.Value = false;
            }
        }
    }
}
