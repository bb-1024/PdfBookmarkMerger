using System.Windows.Input;
using PdfBookmarkMerger.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfBookmarkMerger.WpfApp.Views;

public partial class SettingsDialogWindow : FluentWindow
{
    public SettingsDialogWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public SettingsViewModel ViewModel { get; }

    private void OnOkClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnCancelClick(sender, e);
        }
    }
}
