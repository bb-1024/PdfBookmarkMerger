using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.AvaloniaApp.Views;

public partial class SettingsDialogWindow : Window
{
    public SettingsDialogWindow()
    {
        InitializeComponent();
    }

    public SettingsDialogWindow(SettingsViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SettingsViewModel? ViewModel { get; }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }
}
