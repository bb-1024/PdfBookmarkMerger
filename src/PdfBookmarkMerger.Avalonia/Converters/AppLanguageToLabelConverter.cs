using System.Globalization;
using Avalonia.Data.Converters;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Resources;

namespace PdfBookmarkMerger.AvaloniaApp.Converters;

public sealed class AppLanguageToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AppLanguage.English => Strings.LanguageEnglish,
        _ => Strings.LanguageJapanese,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
