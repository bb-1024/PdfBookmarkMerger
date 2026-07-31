namespace PdfBookmarkMerger.App;

/// <summary>
/// 設定ファイル・ログの保存先。
/// 設定ファイル(settings.json)は、レジストリを使わずアプリの実行ファイルと同じフォルダに保存する
/// (zipを展開したフォルダや任意の場所にそのまま配置して使えるよう、ポータブルな構成にするため)。
/// ログは、実行ファイルの配置場所が読み取り専用の可能性を考慮し、書き込みが保証される
/// ユーザーごとのAppDataフォルダに保存する(Windows/macOS双方でGetFolderPath(ApplicationData)を
/// 用いることで、WPF版・Avalonia版のどちらでも同じロジックで解決できるようにしている)。
/// </summary>
public static class AppPaths
{
    /// <summary>実行ファイル(exe)が配置されているフォルダ。設定ファイルの保存先。</summary>
    public static string AppDirectory { get; } = AppContext.BaseDirectory;

    public static string UserSettingsFilePath => Path.Combine(AppDirectory, "settings.json");

    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfBookmarkMerger");

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}
