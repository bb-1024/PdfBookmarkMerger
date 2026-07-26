using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>
/// PDF仕様上のZoom値(1.0=100%等の倍率)と、UI上でパーセント表示するための文字列を相互変換する。
/// PdfSharpのPdfOutline.Zoomは倍率(1.0=100%)をそのままPDFへ書き込むため、
/// UIでそのまま倍率を入力させると"100"のような直感的な値が10000%として書き込まれてしまう不具合があった。
/// </summary>
public sealed class ZoomPercentToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (d * 100).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? percent / 100
            : BindingOperations.DoNothing;
    }
}
