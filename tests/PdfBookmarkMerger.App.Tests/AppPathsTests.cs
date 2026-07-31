using PdfBookmarkMerger.App;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// 設定ファイルの保存先が、レジストリではなくアプリの実行ファイルと同じフォルダ
/// (AppContext.BaseDirectory)になっていることを検証する。
/// </summary>
public sealed class AppPathsTests
{
    [Fact]
    public void UserSettingsFilePath_IsUnderTheExecutableDirectory_NotUnderAppData()
    {
        AppPaths.UserSettingsFilePath.ShouldStartWith(AppContext.BaseDirectory);
        AppPaths.UserSettingsFilePath.ShouldNotContain("AppData", Case.Insensitive);
        Path.GetFileName(AppPaths.UserSettingsFilePath).ShouldBe("settings.json");
    }

    [Fact]
    public void LogDirectory_RemainsUnderPerUserApplicationData()
    {
        // ログは実行ファイルの配置場所が読み取り専用の可能性を考慮し、AppDataのまま維持する
        // (今回の変更対象は設定ファイルのみ)。
        AppPaths.LogDirectory.ShouldStartWith(AppPaths.AppDataDirectory);
    }
}
