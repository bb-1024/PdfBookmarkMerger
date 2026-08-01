using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PdfBookmarkMerger.App;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.AvaloniaApp.Views;
using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.AvaloniaApp.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private static IClassicDesktopStyleApplicationLifetime? Lifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    private static Window? MainWindow => Lifetime?.MainWindow;

    private static FilePickerFileType PdfFileType => new(Strings.PdfFileTypeName) { Patterns = ["*.pdf"] };

    public async Task<IReadOnlyList<string>> ShowOpenPdfFilesDialogAsync()
    {
        var storageProvider = MainWindow?.StorageProvider;
        if (storageProvider is null)
        {
            return [];
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.OpenPdfFilesDialogTitle,
            AllowMultiple = true,
            FileTypeFilter = [PdfFileType],
        });

        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> ShowOpenFolderDialogAsync()
    {
        var storageProvider = MainWindow?.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.OpenFolderDialogTitle,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowSaveMergedPdfDialogAsync(string suggestedFileName, string? initialDirectory)
    {
        var storageProvider = MainWindow?.StorageProvider;
        if (storageProvider is null)
        {
            return null;
        }

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            startLocation = await storageProvider.TryGetFolderFromPathAsync(new Uri(initialDirectory));
        }

        // WPF版(WpfDialogService)と挙動を揃える: 保存先が未指定の場合は「ドキュメント」フォルダを
        // 既定の開始位置とする(未指定のままだとOS/ピッカー実装依存の挙動になってしまうため)。
        startLocation ??= await storageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveMergedPdfDialogTitle,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "pdf",
            FileTypeChoices = [PdfFileType],
            SuggestedStartLocation = startLocation,
            ShowOverwritePrompt = true,
        });

        return file?.Path.LocalPath;
    }

    public async Task<PdfDocumentPropertiesModel?> ShowPropertiesDialogAsync(PdfDocumentPropertiesModel initial)
    {
        var viewModel = new PropertiesDialogViewModel(initial);
        var window = new PropertiesDialogWindow(viewModel);

        var owner = MainWindow;
        var accepted = owner is not null ? await window.ShowDialog<bool>(owner) : false;

        var result = accepted ? viewModel.ToModel() : null;
        viewModel.Dispose();
        return result;
    }

    public async Task<PdfBookmarkMergerOptions?> ShowSettingsDialogAsync(PdfBookmarkMergerOptions current)
    {
        var viewModel = new SettingsViewModel(current);
        var window = new SettingsDialogWindow(viewModel);

        var owner = MainWindow;
        var accepted = owner is not null && await window.ShowDialog<bool>(owner);

        var result = accepted ? viewModel.ToOptions() : null;
        viewModel.Dispose();

        if (result is not null)
        {
            ThemeApplier.Apply(result.ThemeMode);

            var newLanguage = result.Language ?? AppLanguage.Japanese;
            if (newLanguage != (current.Language ?? AppLanguage.Japanese) && MainWindow is PdfBookmarkMerger.AvaloniaApp.MainWindow mainWindow)
            {
                // x:Static参照はウィンドウ構築・XAML読み込み時点の値で固定されるため、既存ウィンドウの
                // 表示言語をその場で切り替えることはできない。同じViewModel(=読み込み済みファイルや
                // 編集中のしおりツリー等の状態)を引き継いだ新しいウィンドウを構築し、差し替える。
                AppLanguageBootstrapper.ApplyImmediate(newLanguage);
                ReplaceMainWindowForLanguageChange(mainWindow);
            }
        }

        return result;
    }

    private static void ReplaceMainWindowForLanguageChange(PdfBookmarkMerger.AvaloniaApp.MainWindow oldWindow)
    {
        var lifetime = Lifetime;
        if (lifetime is null)
        {
            return;
        }

        var reloaded = new PdfBookmarkMerger.AvaloniaApp.MainWindow(oldWindow.ViewModel)
        {
            Position = oldWindow.Position,
            Width = oldWindow.Width,
            Height = oldWindow.Height,
            WindowState = oldWindow.WindowState == WindowState.Minimized ? WindowState.Normal : oldWindow.WindowState,
        };

        reloaded.Show();
        lifetime.MainWindow = reloaded;
        oldWindow.Close();
    }

    public async Task<int?> ShowLevelCapDialogAsync(int minLevel, int maxLevel)
    {
        var viewModel = new LevelCapDialogViewModel(minLevel, maxLevel);
        var window = new LevelCapDialogWindow(viewModel);

        var owner = MainWindow;
        var accepted = owner is not null && await window.ShowDialog<bool>(owner);

        var result = accepted ? viewModel.SelectedLevel.Value : (int?)null;
        viewModel.Dispose();
        return result;
    }

    public void ShowError(string title, string message)
    {
        var owner = MainWindow;
        var window = new AlertWindow(title, message);
        if (owner is not null)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    public void ShowInfo(string title, string message) => ShowError(title, message);
}
