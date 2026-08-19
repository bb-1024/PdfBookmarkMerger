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

    /// <summary>
    /// ToOptionsは設定ダイアログが編集しないフィールド(LastOutputDirectory等)を、
    /// _sourceからそのまま引き継がなければならない。手動でのフィールド列挙に戻すと、
    /// 新しいフィールドの書き漏れで既定値へ黙って戻ってしまう不具合が再発しうる。
    /// </summary>
    [Fact]
    public void ToOptions_PreservesFieldsNotEditedByTheDialog()
    {
        var source = new PdfBookmarkMergerOptions
        {
            LastOutputDirectory = @"C:\merged-output",
            WindowWidth = 1500,
            WindowHeight = 900,
        };
        var vm = new SettingsViewModel(source);

        var options = vm.ToOptions();

        options.LastOutputDirectory.ShouldBe(@"C:\merged-output");
        options.WindowWidth.ShouldBe(1500);
        options.WindowHeight.ShouldBe(900);
    }

    [Fact]
    public void ToOptions_AppliesTheDialogsEditedFields()
    {
        var vm = new SettingsViewModel(new PdfBookmarkMergerOptions());
        vm.ThemeMode.Value = ThemeMode.Dark;
        vm.ShowPropertiesDialogOnMerge.Value = true;
        vm.ShowMergeAndEditLinksButton.Value = true;
        vm.Language.Value = AppLanguage.English;

        var options = vm.ToOptions();

        options.ThemeMode.ShouldBe(ThemeMode.Dark);
        options.ShowPropertiesDialogOnMerge.ShouldBeTrue();
        options.ShowMergeAndEditLinksButton.ShouldBeTrue();
        options.Language.ShouldBe(AppLanguage.English);
    }
}
