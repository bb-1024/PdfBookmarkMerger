namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// リンク編集画面のプレビュー用に、PDFの1ページをビットマップ(PNG)として描画する。
/// </summary>
public interface IPdfPageRenderer
{
    /// <summary>
    /// 指定ページをPNGとして描画する。<paramref name="scale"/>は1.0が既定DPIでの等倍。
    /// </summary>
    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, float scale, CancellationToken ct = default);

    /// <summary>指定ページのサイズ(pt単位、PDFユーザー空間)を取得する。</summary>
    Task<(double Width, double Height)> GetPageSizeAsync(string filePath, int pageIndex, CancellationToken ct = default);
}
