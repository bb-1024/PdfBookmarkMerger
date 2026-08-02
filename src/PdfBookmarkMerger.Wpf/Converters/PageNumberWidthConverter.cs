using System.Globalization;
using System.Windows.Data;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>
/// 結合前ページ数テキストボックスの幅を、表示する桁数(符号含む)に応じて算出する。
/// 固定幅だと、ページ数の多いPDFで数字が見切れてしまうための対応。
/// </summary>
public sealed class PageNumberWidthConverter : IValueConverter
{
    private const double MinWidth = 32;
    private const double PaddingWidth = 20;
    private const double PerDigitWidth = 9;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int number)
        {
            return MinWidth;
        }

        var digitCount = System.Math.Abs(number).ToString(CultureInfo.InvariantCulture).Length + (number < 0 ? 1 : 0);
        return System.Math.Max(MinWidth, PaddingWidth + (digitCount * PerDigitWidth));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
