using System.Globalization;
using System.Windows.Data;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>LinkGroupInfoを「3ページ目 → 10ページ目」のような表示文字列に変換する(表示は1始まり)。</summary>
public sealed class LinkGroupDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LinkGroupInfo group
            ? string.Format(Strings.LinkGroupDisplayFormat, group.SourcePageIndex + 1, group.TargetPageIndex + 1)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
