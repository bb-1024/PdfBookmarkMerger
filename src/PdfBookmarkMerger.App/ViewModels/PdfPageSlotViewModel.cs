using Reactive.Bindings;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 連続スクロールプレビューの1ページ分のプレースホルダ。コンテナがビューポートに入った時に
/// LinkEditorViewModel.LoadPageSlotAsyncで描画され、ビューポートから外れた時にUnloadPageSlotで
/// 破棄される(数千ページ規模のPDFでも全ページ分のビットマップを同時に保持しないため)。
/// </summary>
public sealed class PdfPageSlotViewModel(int pageIndex)
{
    public int PageIndex { get; } = pageIndex;

    public ReactivePropertySlim<byte[]?> Image { get; } = new(null);

    /// <summary>
    /// このページが現在の操作対象(テキスト選択・リンク作成・ホットスポット表示の対象)かどうか。
    /// 連続スクロール表示では全ページが同時に画面上に存在しうるが、選択・オーバーレイの対象は
    /// 常に1ページのみ(LinkEditorViewModel.CurrentPageIndex)。
    /// </summary>
    public ReactivePropertySlim<bool> IsCurrent { get; } = new(false);
}
