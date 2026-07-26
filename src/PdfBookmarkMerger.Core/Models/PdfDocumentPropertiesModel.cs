namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDFファイルのドキュメントプロパティ(タイトル/作成者等)。
/// 結合後ファイル保存時のプロパティ編集ダイアログのデフォルト値は、
/// 結合対象の先頭PDFファイルのプロパティを流用する。
/// </summary>
public sealed class PdfDocumentPropertiesModel
{
    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    public string Creator { get; set; } = string.Empty;

    public static PdfDocumentPropertiesModel CreateEmpty() => new();

    public PdfDocumentPropertiesModel Clone() => new()
    {
        Title = Title,
        Author = Author,
        Subject = Subject,
        Keywords = Keywords,
        Creator = Creator,
    };
}
