using System.Globalization;
using Avalonia.Data.Converters;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>ConverterParameterと等しいEnum値ならtrue。IsVisibleバインディング等に使用する。</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
