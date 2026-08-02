using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.App.Services;

/// <summary>
/// ファイル/フォルダ選択・保存・プロパティ編集ダイアログの表示。WPF/Avalonia各ヘッドがそれぞれ実装する。
/// </summary>
public interface IDialogService
{
    Task<IReadOnlyList<string>> ShowOpenPdfFilesDialogAsync();

    Task<string?> ShowOpenFolderDialogAsync();

    /// <summary>保存先ダイアログ。キャンセル時はnull。</summary>
    Task<string?> ShowSaveMergedPdfDialogAsync(string suggestedFileName, string? initialDirectory);

    /// <summary>しおり設定ファイル(XML)の保存先ダイアログ。キャンセル時はnull。</summary>
    Task<string?> ShowSaveBookmarkSettingsDialogAsync(string suggestedFileName, string? initialDirectory);

    /// <summary>プロパティ編集ダイアログ。OKでない場合はnull。</summary>
    Task<PdfDocumentPropertiesModel?> ShowPropertiesDialogAsync(PdfDocumentPropertiesModel initial);

    /// <summary>アプリ設定ダイアログ。OKでない場合はnull。OK時はUIフレームワーク固有の表示モード適用も行う。</summary>
    Task<PdfBookmarkMergerOptions?> ShowSettingsDialogAsync(PdfBookmarkMergerOptions current);

    /// <summary>子要素のレベル上限設定ダイアログ。選択可能な範囲はminLevel~maxLevel(ルートから数えた絶対レベル)。キャンセル時はnull。</summary>
    Task<int?> ShowLevelCapDialogAsync(int minLevel, int maxLevel);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
