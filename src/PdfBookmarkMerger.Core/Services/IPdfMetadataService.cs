using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PDFファイルからページ数・しおり(Outline)ツリー・ドキュメントプロパティを読み取る。
/// </summary>
public interface IPdfMetadataService
{
    /// <summary>
    /// 指定ファイルのメタデータを読み取る。しおりが1件も無いPDFの場合はBookmarksが空リストになる。
    /// </summary>
    Task<PdfFileMetadata> ReadMetadataAsync(PdfFileEntry file, CancellationToken ct = default);

    /// <summary>
    /// ページ数のみを高速に取得する。ファイル一覧への追加直後にページ数を表示する用途に使う。
    /// </summary>
    Task<int> ReadPageCountAsync(string filePath, CancellationToken ct = default);
}
