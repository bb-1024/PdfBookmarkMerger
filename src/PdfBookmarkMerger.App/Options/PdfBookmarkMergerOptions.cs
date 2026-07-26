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
}
