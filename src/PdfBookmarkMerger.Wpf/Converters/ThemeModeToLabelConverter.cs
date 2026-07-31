using System.Globalization;
using System.Windows.Data;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;

namespace PdfBookmarkMerger.WpfApp.Converters;

public sealed class ThemeModeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ThemeMode.Light => Strings.ThemeModeLight,
        ThemeMode.Dark => Strings.ThemeModeDark,
        _ => Strings.ThemeModeSystem,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
