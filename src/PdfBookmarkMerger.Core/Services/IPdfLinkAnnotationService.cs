using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// リンク編集画面で作成したリンクを、既存の(結合・しおり設定済みの)PDFファイルへ追記する。
/// </summary>
public interface IPdfLinkAnnotationService
{
    /// <summary>
    /// <paramref name="filePath"/>のPDFへ<paramref name="links"/>のリンク注釈を追加し、同じパスへ保存する。
    /// </summary>
    Task ApplyLinksAsync(string filePath, IReadOnlyList<LinkAnnotationNode> links, CancellationToken ct = default);
}
