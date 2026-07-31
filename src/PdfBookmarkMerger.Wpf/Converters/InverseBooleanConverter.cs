using System.Globalization;
using System.Windows.Data;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>boolを反転する。IsBusy中はコントロールを非活性化する(IsEnabled)用途などに使う。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
