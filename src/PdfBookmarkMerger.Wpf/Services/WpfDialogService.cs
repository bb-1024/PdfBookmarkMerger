using System.Windows;
using Microsoft.Win32;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.WpfApp.Views;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace PdfBookmarkMerger.WpfApp.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<IReadOnlyList<string>> ShowOpenPdfFilesDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "結合対象PDFファイルの選択",
            Filter = "PDFファイル (*.pdf)|*.pdf",
            Multiselect = true,
        };

        var result = dialog.ShowDialog(Application.Current.MainWindow) == true
            ? (IReadOnlyList<string>)dialog.FileNames
            : [];

        return Task.FromResult(result);
    }

    public Task<string?> ShowOpenFolderDialogAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "結合対象PDFフォルダの選択",
        };

        var result = dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
        return Task.FromResult(result);
    }

    public Task<string?> ShowSaveMergedPdfDialogAsync(string suggestedFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "結合後PDFの保存先",
            Filter = "PDFファイル (*.pdf)|*.pdf",
            FileName = suggestedFileName,
            InitialDirectory = string.IsNullOrEmpty(initialDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : initialDirectory,
            OverwritePrompt = true,
        };

        var result = dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
        return Task.FromResult(result);
    }

    public Task<PdfDocumentPropertiesModel?> ShowPropertiesDialogAsync(PdfDocumentPropertiesModel initial)
    {
        var viewModel = new PropertiesDialogViewModel(initial);
        var window = new PropertiesDialogWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        var result = window.ShowDialog() == true ? viewModel.ToModel() : null;
        viewModel.Dispose();
        return Task.FromResult(result);
    }

    public Task<PdfBookmarkMergerOptions?> ShowSettingsDialogAsync(PdfBookmarkMergerOptions current)
    {
        var viewModel = new SettingsViewModel(current);
        var window = new SettingsDialogWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog() == true;
        var result = accepted ? viewModel.ToOptions() : null;
        viewModel.Dispose();

        if (result is not null && Application.Current.MainWindow is { } mainWindow)
        {
            ThemeApplier.Apply(mainWindow, result.ThemeMode);
        }

        return Task.FromResult(result);
    }

    public Task<int?> ShowLevelCapDialogAsync(int minLevel, int maxLevel)
    {
        var viewModel = new LevelCapDialogViewModel(minLevel, maxLevel);
        var window = new LevelCapDialogWindow(viewModel)
        {
            Owner = Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog() == true;
        var result = accepted ? viewModel.SelectedLevel.Value : (int?)null;
        viewModel.Dispose();
        return Task.FromResult(result);
    }

    public void ShowError(string title, string message)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = message,
            Owner = Application.Current.MainWindow,
            CloseButtonText = "OK",
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
        };

        box.ShowDialogAsync();
    }

    public void ShowInfo(string title, string message)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = message,
            Owner = Application.Current.MainWindow,
            CloseButtonText = "OK",
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
        };

        box.ShowDialogAsync();
    }
}
