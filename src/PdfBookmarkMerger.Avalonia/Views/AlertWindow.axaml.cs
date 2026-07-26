using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PdfBookmarkMerger.AvaloniaApp.Views;

public partial class AlertWindow : Window
{
    public AlertWindow()
    {
        InitializeComponent();
    }

    public AlertWindow(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
