namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDF結合処理への入力。ファイル順序・編集済みしおりツリー・出力プロパティ・保存先を保持する。
/// </summary>
public sealed class PdfMergeRequest
{
    public required IReadOnlyList<PdfFileEntry> Files { get; init; }

    public required IReadOnlyList<BookmarkNode> Bookmarks { get; init; }

    public required PdfDocumentPropertiesModel Properties { get; init; }

    public required string OutputPath { get; init; }
}
