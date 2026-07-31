namespace PdfBookmarkMerger.App;

/// <summary>
/// 設定ファイル・ログの保存先。レジストリは一切使わず、いずれもユーザーごとのAppDataフォルダ配下
/// (%AppData%/PdfBookmarkMerger)にまとめて保存する。実行ファイルの配置場所が読み取り専用の
/// 可能性を考慮し、書き込みが保証される場所に統一している。Windows/macOS双方で
/// GetFolderPath(ApplicationData)を用いることで、WPF版・Avalonia版のどちらでも同じロジックで
/// 解決できるようにしている。
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// テストがこのフォルダを差し替えるための環境変数。設定ファイル・ログの保存先が実ユーザーの
    /// AppDataフォルダに一本化されたため、これが無いと単体テストが実際の設定ファイルを上書き・削除
    /// してしまう。通常の実行時はこの環境変数を設定しないため、既定のAppDataフォルダのまま動作する。
    /// キャッシュせず参照のたびに評価することで、他のテストが先にAppPathsへアクセスしていても
    /// (静的フィールドの初回評価タイミングに関わらず)テストごとに確実に差し替えられるようにしている。
    /// </summary>
    private const string OverrideEnvironmentVariable = "PDFBOOKMARKMERGER_APPDATA_DIR";

    public static string AppDataDirectory =>
        Environment.GetEnvironmentVariable(OverrideEnvironmentVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfBookmarkMerger");

    public static string UserSettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}
