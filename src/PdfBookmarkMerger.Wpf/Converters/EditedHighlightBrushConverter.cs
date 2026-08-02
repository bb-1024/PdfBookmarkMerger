using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>
/// 結合前ページ数が編集されている行のテキストボックスを強調表示するための背景色を返す。
/// 半透明色を使うことで、ライト/ダーク双方のテーマ上で違和感なく重ねられるようにしている。
/// </summary>
public sealed class EditedHighlightBrushConverter : IValueConverter
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xB3, 0x00));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HighlightBrush : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
