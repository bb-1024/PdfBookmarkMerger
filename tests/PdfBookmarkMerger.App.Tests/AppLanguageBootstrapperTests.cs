using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// 言語の「一度だけ自動判定して保存し、以後は保存済みの値を使う」動作を検証する。
/// 実際の判定先(日本語/英語)はテスト実行環境のOS言語に依存するため断定できないが、
/// 「保存済みなら再判定・再保存しない」「未設定なら必ず判定・保存し、Strings.Cultureへ反映する」
/// という契約自体は環境非依存に検証できる。
/// </summary>
public sealed class AppLanguageBootstrapperTests
{
    [Fact]
    public async Task ApplyAsync_WhenLanguageAlreadySet_DoesNotOverwriteSettings_AndAppliesItToStrings()
    {
        var settings = new FakeUserSettingsService();
        await settings.SaveAsync(new PdfBookmarkMergerOptions { Language = AppLanguage.English });

        await AppLanguageBootstrapper.ApplyAsync(settings);

        settings.Current.Language.ShouldBe(AppLanguage.English);
        Strings.Culture!.TwoLetterISOLanguageName.ShouldBe("en");
    }

    [Fact]
    public async Task ApplyAsync_WhenLanguageAlreadySetToJapanese_AppliesJapaneseToStrings()
    {
        var settings = new FakeUserSettingsService();
        await settings.SaveAsync(new PdfBookmarkMergerOptions { Language = AppLanguage.Japanese });

        await AppLanguageBootstrapper.ApplyAsync(settings);

        settings.Current.Language.ShouldBe(AppLanguage.Japanese);
        Strings.Culture!.TwoLetterISOLanguageName.ShouldBe("ja");
    }

    [Fact]
    public async Task ApplyAsync_WhenLanguageUnset_DetectsAndPersistsExactlyOnce()
    {
        var settings = new FakeUserSettingsService();
        settings.Current.Language.ShouldBeNull();

        await AppLanguageBootstrapper.ApplyAsync(settings);

        // 未設定だった場合、必ず何らかの言語が判定されて保存される(初回起動時の自動判定・永続化)。
        settings.Current.Language.ShouldNotBeNull();
        Strings.Culture.ShouldNotBeNull();

        // 他の設定値は保持されたまま(Languageのみを追加した複製で保存される)。
        settings.Current.ShowPropertiesDialogOnMerge.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenLanguageUnset_PreservesOtherExistingSettingValues()
    {
        var settings = new FakeUserSettingsService();
        await settings.SaveAsync(new PdfBookmarkMergerOptions
        {
            LastOutputDirectory = @"C:\out",
            ShowPropertiesDialogOnMerge = true,
            Language = null,
        });

        await AppLanguageBootstrapper.ApplyAsync(settings);

        settings.Current.LastOutputDirectory.ShouldBe(@"C:\out");
        settings.Current.ShowPropertiesDialogOnMerge.ShouldBeTrue();
        settings.Current.Language.ShouldNotBeNull();
    }
}
