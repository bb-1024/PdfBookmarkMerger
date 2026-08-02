using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// 現在のしおりツリーを、社内の「しおり設定ファイル仕様」に準拠したXMLファイルとして書き出す。
/// </summary>
public interface IBookmarkSettingsExportService
{
    Task ExportAsync(IReadOnlyList<BookmarkNode> bookmarks, string outputPath, CancellationToken ct = default);
}
