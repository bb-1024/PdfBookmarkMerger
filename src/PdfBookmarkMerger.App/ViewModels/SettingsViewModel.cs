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
    }

    public ReactivePropertySlim<ThemeMode> ThemeMode { get; }

    public ReactivePropertySlim<bool> ShowPropertiesDialogOnMerge { get; }

    public PdfBookmarkMergerOptions ToOptions() => new()
    {
        LastOutputDirectory = _source.LastOutputDirectory,
        WindowWidth = _source.WindowWidth,
        WindowHeight = _source.WindowHeight,
        ThemeMode = ThemeMode.Value,
        ShowPropertiesDialogOnMerge = ShowPropertiesDialogOnMerge.Value,
    };
}
