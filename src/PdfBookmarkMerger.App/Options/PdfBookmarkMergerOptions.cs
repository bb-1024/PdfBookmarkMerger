namespace PdfBookmarkMerger.App.Options;

/// <summary>
/// appsettings.json / ユーザー設定ファイルの"PdfBookmarkMerger"セクションにバインドされるアプリ設定。
/// </summary>
public sealed class PdfBookmarkMergerOptions
{
    public const string SectionName = "PdfBookmarkMerger";

    /// <summary>最後にPDFを保存したフォルダ。次回保存ダイアログの初期フォルダに使用する。</summary>
    public string? LastOutputDirectory { get; set; }

    public double WindowWidth { get; set; } = 1100;

    public double WindowHeight { get; set; } = 750;

    /// <summary>表示モード(ライト/ダーク/システム設定)。既定はシステム設定に追従。</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    /// <summary>PDF結合時にプロパティ編集ダイアログを表示するかどうか。既定は表示しない。</summary>
    public bool ShowPropertiesDialogOnMerge { get; set; }

    /// <summary>しおり編集画面に「結合してリンク編集へ進む」ボタンを表示するかどうか。
    /// 既定(設定ファイル未読み込み時を含む)は表示しない。</summary>
    public bool ShowMergeAndEditLinksButton { get; set; }

    /// <summary>
    /// UI表示言語。nullは「未設定」を意味し、初回起動(または本設定項目導入前のバージョンからの
    /// 移行後の初回起動)時にAppLanguageBootstrapperがシステム言語から自動判定し、
    /// この値として保存する。以降は明示的にこの値が使われ、再判定は行わない。
    /// </summary>
    public AppLanguage? Language { get; set; }

    /// <summary>
    /// 全プロパティをコピーした複製を返す。設定の一部だけを書き換えて保存する呼び出し元
    /// (MainWindowViewModel.MergeCoreAsync・SettingsViewModel.ToOptions)は、フィールドを
    /// 手動で1つずつ列挙するのではなく必ずこれを経由すること。新しいプロパティを追加する際に
    /// 呼び出し側の書き漏れで既定値へ黙って戻ってしまう不具合(実際にShowMergeAndEditLinksButtonで
    /// 一度発生した)を防ぐため。
    /// </summary>
    public PdfBookmarkMergerOptions Clone() => new()
    {
        LastOutputDirectory = LastOutputDirectory,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        ThemeMode = ThemeMode,
        ShowPropertiesDialogOnMerge = ShowPropertiesDialogOnMerge,
        ShowMergeAndEditLinksButton = ShowMergeAndEditLinksButton,
        Language = Language,
    };
}
