using System.Globalization;
using System.Windows.Data;
using PdfBookmarkMerger.App.Resources;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>ページ数(int?)を「12ページ」のような表示文字列に変換する。</summary>
public sealed class PageCountToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count ? string.Format(Strings.PageCountFormat, count) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
