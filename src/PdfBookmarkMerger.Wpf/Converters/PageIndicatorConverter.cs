using System.Globalization;
using System.Windows.Data;
using PdfBookmarkMerger.App.Resources;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>0始まりの現在ページ番号とページ総数から「3 / 10 ページ」のような表示文字列を作る。</summary>
public sealed class PageIndicatorConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var currentPageIndex = values.Length > 0 && values[0] is int i ? i : 0;
        var pageCount = values.Length > 1 && values[1] is int c ? c : 0;
        return string.Format(Strings.PageIndicatorFormat, currentPageIndex + 1, pageCount);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
