using System.Text.Json.Serialization;

namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDFしおり(Outline)1件分の情報。Title/Pages(ページ・表示方法・座標)/Action(常にGoTo)/Open状態/子しおりを保持する。
/// PDF結合前は抽出元PDFのページ番号(OriginalPageIndex)のみを持ち、結合順が確定した時点で
/// 直前までのファイルの総ページ数を加算したMergedPageIndexが設定される。
/// </summary>
public sealed class BookmarkNode
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    /// <summary>ジャンプ先ページが属する<see cref="PdfFileEntry.Id"/>。</summary>
    public required Guid SourceFileEntryId { get; init; }

    /// <summary>抽出元PDFファイル内での0始まりページ番号。</summary>
    public required int OriginalPageIndex { get; init; }

    /// <summary>結合後PDFにおける0始まりページ番号。しおり抽出直後に計算される。</summary>
    public int? MergedPageIndex { get; set; }

    /// <summary>GoToアクションの表示方法。</summary>
    public BookmarkDestinationType DestinationType { get; set; } = BookmarkDestinationType.Fit;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Right { get; set; }

    public double? Bottom { get; set; }

    public double? Zoom { get; set; }

    /// <summary>しおりパネルで初期状態から展開表示するか(PdfOutline.Opened相当)。</summary>
    public bool IsOpen { get; set; }

    /// <summary>PdfSharpが生成できるActionは常にGoToのみ。将来の拡張に備え保持する。</summary>
    public string ActionType => "GoTo";

    /// <summary>
    /// setterを持たないため、既定ではSystem.Text.Jsonの逆シリアル化時に無視され空のまま残ってしまう
    /// (Populateを明示しないと読み取り専用コレクションへは値を書き込まない仕様のため)。
    /// Undo用スナップショット(JSONラウンドトリップ)で子孫が失われないよう明示的に指定する。
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<BookmarkNode> Children { get; } = [];

    /// <summary>自身と子孫すべての深いコピーを返す(Idは複製ごとに新規採番される)。</summary>
    public BookmarkNode Clone()
    {
        var clone = new BookmarkNode
        {
            SourceFileEntryId = SourceFileEntryId,
            OriginalPageIndex = OriginalPageIndex,
            MergedPageIndex = MergedPageIndex,
            Title = Title,
            DestinationType = DestinationType,
            Left = Left,
            Top = Top,
            Right = Right,
            Bottom = Bottom,
            Zoom = Zoom,
            IsOpen = IsOpen,
        };
        clone.Children.AddRange(Children.Select(c => c.Clone()));
        return clone;
    }
}
