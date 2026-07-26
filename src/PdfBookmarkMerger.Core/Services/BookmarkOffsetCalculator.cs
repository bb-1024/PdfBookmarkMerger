using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// ファイル結合順に基づき、各しおりのMergedPageIndex(結合後PDFにおけるページ番号)を計算する。
/// オフセットは「自身が属するファイルより前に綴じ込まれる全ファイルの総ページ数」。
/// </summary>
public static class BookmarkOffsetCalculator
{
    /// <summary>
    /// ファイル順序と、ファイル毎の実効しおりリスト(<see cref="MissingBookmarkFallback"/>適用後)・
    /// メタデータから、結合後しおりツリー(ファイル順に連結)を組み立てる。
    /// 入力(<paramref name="effectiveBookmarksByFileId"/>・<paramref name="metadataByFileId"/>)は変更せず、
    /// MergedPageIndexを設定した複製ツリーを新たに返す(非破壊)。
    /// 各ノードのMergedPageIndexは表示用の副次情報であり、実際のPDF結合ではSourceFileEntryId+OriginalPageIndexで
    /// ジャンプ先ページを特定する。
    /// </summary>
    public static List<BookmarkNode> ComputeMergedBookmarks(
        IReadOnlyList<PdfFileEntry> orderedFiles,
        IReadOnlyDictionary<Guid, IReadOnlyList<BookmarkNode>> effectiveBookmarksByFileId,
        IReadOnlyDictionary<Guid, PdfFileMetadata> metadataByFileId)
    {
        var result = new List<BookmarkNode>();
        var offset = 0;

        foreach (var file in orderedFiles)
        {
            if (!metadataByFileId.TryGetValue(file.Id, out var metadata) ||
                !effectiveBookmarksByFileId.TryGetValue(file.Id, out var bookmarks))
            {
                continue;
            }

            foreach (var node in bookmarks)
            {
                var clone = node.Clone();
                ApplyOffset(clone, offset);
                result.Add(clone);
            }

            offset += metadata.PageCount;
        }

        return result;
    }

    private static void ApplyOffset(BookmarkNode node, int offset)
    {
        node.MergedPageIndex = node.OriginalPageIndex + offset;
        foreach (var child in node.Children)
        {
            ApplyOffset(child, offset);
        }
    }
}
