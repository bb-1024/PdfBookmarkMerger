using System.Windows;
using Microsoft.Win32;
using PdfBookmarkMerger.App;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.WpfApp.Views;
using AppThemeMode = PdfBookmarkMerger.App.Options.ThemeMode;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace PdfBookmarkMerger.WpfApp.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<IReadOnlyList<string>> ShowOpenPdfFilesDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.OpenPdfFilesDialogTitle,
            Filter = Strings.PdfFileFilterWpf,
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
            Title = Strings.OpenFolderDialogTitle,
        };

        var result = dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
        return Task.FromResult(result);
    }

    public Task<string?> ShowSaveMergedPdfDialogAsync(string suggestedFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.SaveMergedPdfDialogTitle,
            Filter = Strings.PdfFileFilterWpf,
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

        if (result is not null && Application.Current.MainWindow is MainWindow mainWindow)
        {
            ThemeApplier.Apply(mainWindow, result.ThemeMode);

            var newLanguage = result.Language ?? AppLanguage.Japanese;
            if (newLanguage != (current.Language ?? AppLanguage.Japanese))
            {
                // x:Static参照はウィンドウ構築・XAML読み込み時点の値で固定されるため、既存ウィンドウの
                // 表示言語をその場で切り替えることはできない。同じViewModel(=読み込み済みファイルや
                // 編集中のしおりツリー等の状態)を引き継いだ新しいウィンドウを構築し、差し替える。
                AppLanguageBootstrapper.ApplyImmediate(newLanguage);
                mainWindow = ReplaceMainWindowForLanguageChange(mainWindow, result.ThemeMode);
            }
        }

        return Task.FromResult(result);
    }

    private static MainWindow ReplaceMainWindowForLanguageChange(MainWindow oldWindow, AppThemeMode themeMode)
    {
        var reloaded = new MainWindow(oldWindow.ViewModel)
        {
            Left = oldWindow.Left,
            Top = oldWindow.Top,
            Width = oldWindow.Width,
            Height = oldWindow.Height,
            WindowState = oldWindow.WindowState == WindowState.Minimized ? WindowState.Normal : oldWindow.WindowState,
        };

        ThemeApplier.Apply(reloaded, themeMode);
        reloaded.Show();
        Application.Current.MainWindow = reloaded;
        oldWindow.Close();

        return reloaded;
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
            CloseButtonText = Strings.OkButton,
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
            CloseButtonText = Strings.OkButton,
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false,
        };

        box.ShowDialogAsync();
    }
}
