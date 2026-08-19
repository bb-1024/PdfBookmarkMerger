using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>値がnullでなければVisible、nullならCollapsedを返す。リンク編集画面のジャンプ先選択パネルの表示制御に使う。</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
