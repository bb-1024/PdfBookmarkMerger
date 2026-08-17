using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.ViewModels;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// 設定ダイアログにリリースバージョンを表示する機能(v1.2.2〜)の回帰テスト。
/// </summary>
public sealed class SettingsViewModelTests
{
    [Fact]
    public void AppVersion_IsPopulatedFromTheBuiltAssemblyVersion()
    {
        var vm = new SettingsViewModel(new PdfBookmarkMergerOptions());

        vm.AppVersion.ShouldNotBeNullOrWhiteSpace();
        vm.AppVersion.ShouldMatch(@"^\d+\.\d+\.\d+", "Directory.Build.propsのVersion(例: 1.2.2)がそのまま反映されているはず");
    }
}
