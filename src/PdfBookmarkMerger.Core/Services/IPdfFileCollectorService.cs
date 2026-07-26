namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// ドラッグ&ドロップ／ダイアログで渡されたパス群を、結合対象PDFファイルパスの一覧に展開する。
/// </summary>
public interface IPdfFileCollectorService
{
    /// <summary>
    /// パス群(ファイル・フォルダ混在可)からPDFファイルパスの一覧を返す。
    /// フォルダが渡された場合はその直下(子フォルダは対象外)の*.pdfのみを対象とする。
    /// 存在しないパスや非PDFファイルは無視する。
    /// </summary>
    IReadOnlyList<string> ExpandToPdfFilePaths(IEnumerable<string> droppedPaths);
}
