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
        // SystemThemeWatcher.Watchは重複登録を防止しないため、設定ダイアログでOKを押すたびに
        // (モードが変わっていなくても)無条件でWatchすると同一ウィンドウが監視リストに何度も
        // 積み重なってしまう。呼び出しのたびにまず解除してから、必要な状態を作り直す。
        // UnWatchは「まだLoadedしていないウィンドウ」に対して呼ぶと例外になるため、
        // アプリ起動直後(Show前)の初回呼び出しはスキップする。
        if (window.IsLoaded)
        {
            SystemThemeWatcher.UnWatch(window);
        }

        if (mode == AppThemeMode.System)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.None);
            return;
        }

        var theme = mode == AppThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None);
    }
}
