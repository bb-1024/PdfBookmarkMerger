namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// リンク編集画面で作成する1件のページ内リンク。結合・しおり設定済みの単一PDF(すでに実ファイルとして
/// 存在する)に対して追加するため、しおりと異なりSourceFileEntryId等の複数ファイル対応は持たない。
/// </summary>
public sealed class LinkAnnotationNode
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// 複数行にまたがるテキスト選択から生成された一連のLinkAnnotationNodeで共有するID。
    /// 一覧表示・編集・削除はこの単位でまとめて扱う(単一行選択の場合はGroupId == Id)。
    /// </summary>
    public required Guid GroupId { get; init; }

    /// <summary>ホットスポットがあるページ(結合後PDF内、0始まり)。</summary>
    public required int SourcePageIndex { get; init; }

    /// <summary>ホットスポットの矩形(PDFユーザー空間、pt)。</summary>
    public required PdfRect SourceRect { get; init; }

    /// <summary>ジャンプ先ページ(結合後PDF内、0始まり)。</summary>
    public required int TargetPageIndex { get; init; }

    /// <summary>GoToアクションの表示方法。</summary>
    public BookmarkDestinationType DestinationType { get; set; } = BookmarkDestinationType.XYZ;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Right { get; set; }

    public double? Bottom { get; set; }

    public double? Zoom { get; set; }
}
