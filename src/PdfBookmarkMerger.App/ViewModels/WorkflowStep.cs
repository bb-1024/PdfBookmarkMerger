namespace PdfBookmarkMerger.App.ViewModels;

public enum WorkflowStep
{
    /// <summary>手順1: 結合対象PDFファイルの指定・並べ替え。</summary>
    SelectFiles,

    /// <summary>手順2・3: しおり情報抽出結果の確認・編集。</summary>
    EditBookmarks,
}
