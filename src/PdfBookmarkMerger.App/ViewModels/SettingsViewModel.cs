using System.Reflection;
using PdfBookmarkMerger.App.Options;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// アプリ設定ダイアログのViewModel。表示モード(ライト/ダーク/システム設定)と、
/// PDF結合時のプロパティ編集ダイアログ表示有無を編集する。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly PdfBookmarkMergerOptions _source;

    public SettingsViewModel(PdfBookmarkMergerOptions initial)
    {
        _source = initial;

        ThemeMode = new ReactivePropertySlim<ThemeMode>(initial.ThemeMode).AddTo(Disposables);
        ShowPropertiesDialogOnMerge = new ReactivePropertySlim<bool>(initial.ShowPropertiesDialogOnMerge).AddTo(Disposables);
        Language = new ReactivePropertySlim<AppLanguage>(initial.Language ?? AppLanguage.Japanese).AddTo(Disposables);
    }

    /// <summary>
    /// リリース版のバージョン表示(例: "1.2.2")。Directory.Build.propsの&lt;Version&gt;がビルド時に
    /// AssemblyInformationalVersionAttributeへ書き込まれる値をそのまま使う(WPF版・Avalonia版いずれも
    /// 同じDirectory.Build.propsを参照するため、App.dllのバージョンで代表できる)。
    /// </summary>
    public string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? string.Empty;

    public ReactivePropertySlim<ThemeMode> ThemeMode { get; }

    public ReactivePropertySlim<bool> ShowPropertiesDialogOnMerge { get; }

    /// <summary>
    /// 表示言語。OK押下時にThemeModeと同様その場で反映される(WpfDialogService/AvaloniaDialogServiceの
    /// ShowSettingsDialogAsync参照)。x:Static参照はウィンドウ構築時点の値で固定されるため、
    /// 呼び出し側はStrings.Culture切り替え後にメインウィンドウを再構築する形で実現している。
    /// </summary>
    public ReactivePropertySlim<AppLanguage> Language { get; }

    public PdfBookmarkMergerOptions ToOptions() => new()
    {
        LastOutputDirectory = _source.LastOutputDirectory,
        WindowWidth = _source.WindowWidth,
        WindowHeight = _source.WindowHeight,
        ThemeMode = ThemeMode.Value,
        ShowPropertiesDialogOnMerge = ShowPropertiesDialogOnMerge.Value,
        Language = Language.Value,
    };
}
