namespace PdfBookmarkMerger.App;

/// <summary>
/// ユーザー設定・ログの保存先。Windows/macOS双方でGetFolderPath(ApplicationData)を用いることで、
/// WPF版・Avalonia版のどちらでも同じロジックで解決できるようにしている。
/// </summary>
public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfBookmarkMerger");

    public static string UserSettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");
}
