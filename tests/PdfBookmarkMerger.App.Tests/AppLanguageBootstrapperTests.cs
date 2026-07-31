using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// 言語の「一度だけ自動判定して保存し、以後は保存済みの値を使う」動作を検証する。
/// 実際の判定先(日本語/英語)はテスト実行環境のOS言語に依存するため断定できないが、
/// 「保存済みなら再判定・再保存しない」「未設定なら必ず判定・保存し、Strings.Cultureへ反映する」
/// という契約自体は環境非依存に検証できる。
/// </summary>
public sealed class AppLanguageBootstrapperTests : IDisposable
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

    /// <summary>
    /// 実際に発生した不具合の再現テスト: App.OnStartup/OnFrameworkInitializationCompletedと同じく、
    /// 初回起動(Language未設定)時にAppLanguageBootstrapper.ApplyAsync(...).GetAwaiter().GetResult()を
    /// UIスレッドで同期的にブロックして呼んでも、デッドロックせずに完了することを検証する
    /// (実サービスのUserSettingsServiceを使う。FakeUserSettingsServiceはawaitを一切含まないため、
    /// この種のデッドロックを再現・検出できない)。
    /// </summary>
    [Fact]
    public void ApplyAsync_CalledSynchronouslyFromUiStartup_WithLanguageUnset_DoesNotDeadlock()
    {
        // AppPaths.AppDataDirectoryは実ユーザーのAppDataフォルダを指すため、環境変数で一時フォルダに
        // 差し替えてから使う。差し替えないと、このテストが実際のsettings.jsonを上書き・削除してしまう。
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("PDFBOOKMARKMERGER_APPDATA_DIR", tempDirectory);
        try
        {
            var service = new UserSettingsService(new FakeOptionsMonitor(), NullLogger<UserSettingsService>.Instance);
            var completed = false;

            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
                AppLanguageBootstrapper.ApplyAsync(service).GetAwaiter().GetResult();
                completed = true;
            });
            thread.Start();

            var joinedInTime = thread.Join(TimeSpan.FromSeconds(5));

            joinedInTime.ShouldBeTrue(
                "AppLanguageBootstrapper.ApplyAsyncが5秒以内に完了しませんでした(初回起動時のデッドロック再発の疑い)。");
            completed.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PDFBOOKMARKMERGER_APPDATA_DIR", null);
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    public void Dispose() => Strings.Culture = null;

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // 意図的に何もしない(ブロックされたUIスレッド自身がディスパッチループも兼ねる状況を模擬)。
        }
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<PdfBookmarkMergerOptions>
    {
        public PdfBookmarkMergerOptions CurrentValue { get; } = new();

        public PdfBookmarkMergerOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<PdfBookmarkMergerOptions, string> listener) => new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
