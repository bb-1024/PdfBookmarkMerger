using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.AvaloniaApp.Views;
using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.AvaloniaApp.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

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
        }

        return result;
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
