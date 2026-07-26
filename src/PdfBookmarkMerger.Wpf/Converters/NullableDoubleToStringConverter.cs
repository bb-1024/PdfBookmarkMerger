using System.Globalization;
using System.Windows.Data;

namespace PdfBookmarkMerger.WpfApp.Converters;

public sealed class NullableDoubleToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : Binding.DoNothing;
    }
}
