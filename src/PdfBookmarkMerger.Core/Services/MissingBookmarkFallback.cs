using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// しおりを1件も持たないPDFファイルについて、ファイル名(拡張子なし)をタイトルとするしおりを補った
/// 「実効しおりリスト」をファイルごとに解決する。表示方法(DestinationType)は直前のファイルの設定を
/// 参考にする(表示位置の座標は引き継がない)。直前のファイルが無い、または直前のファイルにも
/// 参考にできる表示方法が無い場合は既定値を用いる。
/// </summary>
public static class MissingBookmarkFallback
{
    private const BookmarkDestinationType DefaultDestinationType = BookmarkDestinationType.Fit;

    /// <summary>
    /// ファイルごとの実効しおりリストを返す。<paramref name="metadataByFileId"/>自体は変更しない
    /// (しおりを持たないファイルの補完分は、都度新規に生成した非破壊な結果として返す)。
    /// </summary>
    public static Dictionary<Guid, IReadOnlyList<BookmarkNode>> ResolveEffectiveBookmarks(
        IReadOnlyList<PdfFileEntry> orderedFiles,
        IReadOnlyDictionary<Guid, PdfFileMetadata> metadataByFileId)
    {
        var result = new Dictionary<Guid, IReadOnlyList<BookmarkNode>>();
        var previousDestinationType = DefaultDestinationType;

        foreach (var file in orderedFiles)
        {
            if (!metadataByFileId.TryGetValue(file.Id, out var metadata))
            {
                continue;
            }

            IReadOnlyList<BookmarkNode> bookmarks = metadata.Bookmarks.Count == 0
                ? [CreateFallbackBookmark(file, previousDestinationType)]
                : metadata.Bookmarks;

            result[file.Id] = bookmarks;
            previousDestinationType = bookmarks[0].DestinationType;
        }

        return result;
    }

    private static BookmarkNode CreateFallbackBookmark(PdfFileEntry file, BookmarkDestinationType destinationType) => new()
    {
        SourceFileEntryId = file.Id,
        OriginalPageIndex = 0,
        Title = Path.GetFileNameWithoutExtension(file.FilePath),
        DestinationType = destinationType,
    };
}
