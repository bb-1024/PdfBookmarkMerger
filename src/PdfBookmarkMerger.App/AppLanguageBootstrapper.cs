using System.Globalization;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;

namespace PdfBookmarkMerger.App;

/// <summary>
/// アプリ起動時に表示言語を確定させ、<see cref="Strings.Culture"/>へ反映する。
/// WPF版・Avalonia版いずれのMainWindow構築より前に、一度だけ呼び出す必要がある
/// (呼び出し後にウィンドウ・ダイアログを構築しないと、x:Static参照が既定言語のまま固まってしまう)。
/// </summary>
public static class AppLanguageBootstrapper
{
    /// <summary>
    /// 設定に保存済みの言語があればそれを使う。未設定(初回起動、または本項目導入前バージョンからの
    /// 移行後の初回起動)の場合は、OSのUI言語から日本語/英語を判定し、以後再判定しないよう保存する。
    /// システム言語が読み取れない場合は日本語を既定とする。
    /// </summary>
    public static async Task ApplyAsync(IUserSettingsService userSettings, CancellationToken ct = default)
    {
        var current = userSettings.Current;
        var language = current.Language;

        if (language is null)
        {
            language = DetectSystemLanguage();
            await userSettings.SaveAsync(CloneWithLanguage(current, language.Value), ct).ConfigureAwait(false);
        }

        Strings.Culture = ToCultureInfo(language.Value);
    }

    private static AppLanguage DetectSystemLanguage()
    {
        try
        {
            return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.Japanese
                : AppLanguage.English;
        }
        catch (Exception)
        {
            // OSのUI言語が読み取れない(取得時に例外が発生する)場合は日本語を既定とする。
            return AppLanguage.Japanese;
        }
    }

    private static CultureInfo ToCultureInfo(AppLanguage language) =>
        CultureInfo.GetCultureInfo(language == AppLanguage.English ? "en" : "ja");

    private static PdfBookmarkMergerOptions CloneWithLanguage(PdfBookmarkMergerOptions source, AppLanguage language) =>
        new()
        {
            LastOutputDirectory = source.LastOutputDirectory,
            WindowWidth = source.WindowWidth,
            WindowHeight = source.WindowHeight,
            ThemeMode = source.ThemeMode,
            ShowPropertiesDialogOnMerge = source.ShowPropertiesDialogOnMerge,
            Language = language,
        };
}
