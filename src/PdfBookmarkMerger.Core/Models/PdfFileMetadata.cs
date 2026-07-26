namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// 1個のPDFファイルから読み取ったページ数・しおりツリー・プロパティ。
/// </summary>
public sealed class PdfFileMetadata
{
    public required Guid FileEntryId { get; init; }

    public required int PageCount { get; init; }

    public required List<BookmarkNode> Bookmarks { get; init; }

    public required PdfDocumentPropertiesModel Properties { get; init; }
}
