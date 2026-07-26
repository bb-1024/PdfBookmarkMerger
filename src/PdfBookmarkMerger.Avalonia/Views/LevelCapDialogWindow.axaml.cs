using Avalonia.Controls;
using Avalonia.Interactivity;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.AvaloniaApp.Views;

public partial class LevelCapDialogWindow : Window
{
    public LevelCapDialogWindow()
    {
        InitializeComponent();
    }

    public LevelCapDialogWindow(LevelCapDialogViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public LevelCapDialogViewModel? ViewModel { get; }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
