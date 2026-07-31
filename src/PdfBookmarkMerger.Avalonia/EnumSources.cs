using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.AvaloniaApp;

/// <summary>XAMLのx:Staticから参照するEnum一覧(ComboBoxのItemsSource用)。</summary>
public static class EnumSources
{
    public static BookmarkDestinationType[] DestinationTypeValues { get; } = Enum.GetValues<BookmarkDestinationType>();

    public static ThemeMode[] ThemeModeValues { get; } = Enum.GetValues<ThemeMode>();

    public static AppLanguage[] AppLanguageValues { get; } = Enum.GetValues<AppLanguage>();
}
