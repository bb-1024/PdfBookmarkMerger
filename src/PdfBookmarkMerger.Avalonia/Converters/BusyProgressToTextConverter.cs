using System.Globalization;
using Avalonia.Data.Converters;
using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>BusyProgressInfoを「12 / 340件 (処理中: a.pdf, b.pdf)」のような表示文字列に変換する。</summary>
public sealed class BusyProgressToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BusyProgressInfo info)
        {
            return string.Empty;
        }

        var detail = info.CurrentFileNames.Count > 0 ? string.Join(", ", info.CurrentFileNames) : null;
        return detail is null
            ? string.Format(Strings.BusyProgressCountOnlyFormat, info.CompletedCount, info.TotalCount)
            : string.Format(Strings.BusyProgressWithDetailFormat, info.CompletedCount, info.TotalCount, detail);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
