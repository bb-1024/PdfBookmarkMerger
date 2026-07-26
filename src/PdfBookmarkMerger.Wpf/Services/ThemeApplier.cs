using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using AppThemeMode = PdfBookmarkMerger.App.Options.ThemeMode;

namespace PdfBookmarkMerger.WpfApp.Services;

/// <summary>
/// ThemeMode設定(ライト/ダーク/システム設定)を実際のWPF-UIテーマへ適用する。
/// アプリ起動時・設定ダイアログでの変更時の両方から呼び出す。
/// </summary>
public static class ThemeApplier
{
    public static void Apply(Window window, AppThemeMode mode)
    {
        if (mode == AppThemeMode.System)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.None);
            return;
        }

        // 明示的なライト/ダーク指定時は、システム設定への自動追従を止めてから固定で適用する。
        // UnWatchは「まだLoadedしていないウィンドウ」に対して呼ぶと例外になるため、
        // アプリ起動直後(Show前)にLight/Darkが指定されているケースはスキップする。
        if (window.IsLoaded)
        {
            SystemThemeWatcher.UnWatch(window);
        }

        var theme = mode == AppThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None);
    }
}
