using System.Globalization;
using Avalonia.Data.Converters;
using PdfBookmarkMerger.App.Resources;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>0始まりの現在ページ番号とページ総数から「3 / 10 ページ」のような表示文字列を作る。</summary>
public sealed class PageIndicatorConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var currentPageIndex = values.Count > 0 && values[0] is int i ? i : 0;
        var pageCount = values.Count > 1 && values[1] is int c ? c : 0;
        return string.Format(Strings.PageIndicatorFormat, currentPageIndex + 1, pageCount);
    }
}
