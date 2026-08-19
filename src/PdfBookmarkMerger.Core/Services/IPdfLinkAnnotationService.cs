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

    /// <summary>
    /// <paramref name="filePath"/>のPDFに既に含まれているリンク注釈(/Subtype /Link)を読み取る。
    /// 結合元ファイルに元々含まれていたリンクや、以前にこのアプリで設定・保存したリンクを、
    /// リンク編集画面の一覧に表示するために使う。ドキュメント内ページへのGoToジャンプ先を
    /// 持たないもの(外部URLリンク等)は読み飛ばす。
    /// </summary>
    Task<IReadOnlyList<LinkAnnotationNode>> ReadExistingLinksAsync(string filePath, CancellationToken ct = default);
}
