using Avalonia;
using Avalonia.Styling;
using PdfBookmarkMerger.App.Options;

namespace PdfBookmarkMerger.AvaloniaApp.Services;

/// <summary>ThemeMode設定(ライト/ダーク/システム設定)をAvaloniaのRequestedThemeVariantへ適用する。</summary>
public static class ThemeApplier
{
    public static void Apply(ThemeMode mode)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
