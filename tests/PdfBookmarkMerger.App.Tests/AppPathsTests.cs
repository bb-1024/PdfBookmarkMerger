using PdfBookmarkMerger.App;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// 設定ファイル・ログの保存先が、レジストリではなく同一のAppDataフォルダ
/// (%AppData%/PdfBookmarkMerger)配下に一貫して保存されることを検証する。
/// </summary>
public sealed class AppPathsTests
{
    [Fact]
    public void UserSettingsFilePath_IsUnderAppDataDirectory_SameAsLogDirectory()
    {
        AppPaths.UserSettingsFilePath.ShouldStartWith(AppPaths.AppDataDirectory);
        Path.GetFileName(AppPaths.UserSettingsFilePath).ShouldBe("settings.json");
    }

    [Fact]
    public void LogDirectory_IsUnderAppDataDirectory_SameAsUserSettingsFilePath()
    {
        AppPaths.LogDirectory.ShouldStartWith(AppPaths.AppDataDirectory);
    }

    [Fact]
    public void AppDataDirectory_IsUnderPerUserApplicationData_NotProgramInstallLocation()
    {
        AppPaths.AppDataDirectory.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AppPaths.AppDataDirectory.ShouldNotStartWith(AppContext.BaseDirectory);
    }
}
