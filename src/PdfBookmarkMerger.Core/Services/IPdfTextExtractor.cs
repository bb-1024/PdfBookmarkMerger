using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// リンク編集画面でのテキスト選択(文字単位)のために、PDFページから文字とその位置を抽出する。
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>指定ページの文字(グリフ)を、読み順に並べて返す。</summary>
    Task<IReadOnlyList<PdfTextLetter>> ExtractLettersAsync(string filePath, int pageIndex, CancellationToken ct = default);
}
