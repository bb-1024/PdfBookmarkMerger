using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PdfBookmarkMerger.WpfApp.Converters;

/// <summary>PNGバイト列(byte[]?)をWPFの<see cref="BitmapImage"/>へ変換する。リンク編集画面のページプレビュー用。</summary>
public sealed class ByteArrayToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
        {
            return null;
        }

        var image = new BitmapImage();
        using (var stream = new MemoryStream(bytes))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
        }

        image.Freeze();
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
