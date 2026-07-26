using System.Globalization;
using Avalonia.Data.Converters;
using PdfBookmarkMerger.App.Options;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

public sealed class ThemeModeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ThemeMode.Light => "ライト",
        ThemeMode.Dark => "ダーク",
        _ => "システム設定(既定)",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
