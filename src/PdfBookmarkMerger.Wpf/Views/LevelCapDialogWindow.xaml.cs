using PdfBookmarkMerger.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfBookmarkMerger.WpfApp.Views;

public partial class LevelCapDialogWindow : FluentWindow
{
    public LevelCapDialogWindow(LevelCapDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public LevelCapDialogViewModel ViewModel { get; }

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
}
