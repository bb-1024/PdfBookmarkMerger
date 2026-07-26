using PdfBookmarkMerger.App.ViewModels;
using Wpf.Ui.Controls;

namespace PdfBookmarkMerger.WpfApp.Views;

public partial class PropertiesDialogWindow : FluentWindow
{
    public PropertiesDialogWindow(PropertiesDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        // OKボタンに初期フォーカスを当て、Enterキーでそのまま確定できるようにする。
        Loaded += (_, _) => OkButton.Focus();
    }

    public PropertiesDialogViewModel ViewModel { get; }

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
