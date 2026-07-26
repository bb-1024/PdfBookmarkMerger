namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// 結合対象PDFファイル一覧の1エントリ。UI上の並び順がそのまま結合順になる。
/// </summary>
public sealed class PdfFileEntry
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string FilePath { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>ファイル確定（しおり読み込み）前は null。</summary>
    public int? PageCount { get; set; }
}
