using Avalonia.Controls;
using Avalonia.Interactivity;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.AvaloniaApp.Views;

public partial class PropertiesDialogWindow : Window
{
    public PropertiesDialogWindow()
    {
        InitializeComponent();

        // OKボタンに初期フォーカスを当て、Enterキーでそのまま確定できるようにする。
        Loaded += (_, _) => OkButton.Focus();
    }

    public PropertiesDialogWindow(PropertiesDialogViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public PropertiesDialogViewModel? ViewModel { get; }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
