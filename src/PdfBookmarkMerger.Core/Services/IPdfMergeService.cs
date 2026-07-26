using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PDFファイル群を結合し、しおりツリーとプロパティを適用して新規ファイルに保存する。
/// </summary>
public interface IPdfMergeService
{
    Task MergeAsync(PdfMergeRequest request, CancellationToken ct = default);
}
