using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>ConverterParameterと等しいEnum値ならVisible、それ以外はCollapsed。</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
