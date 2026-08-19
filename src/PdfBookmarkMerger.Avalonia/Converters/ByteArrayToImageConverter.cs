using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

/// <summary>PNGバイト列(byte[]?)をAvaloniaの<see cref="Bitmap"/>へ変換する。リンク編集画面のページプレビュー用。</summary>
public sealed class ByteArrayToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
