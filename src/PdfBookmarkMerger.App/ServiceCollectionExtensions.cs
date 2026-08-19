using Microsoft.Extensions.DependencyInjection;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.App.ViewModels;

namespace PdfBookmarkMerger.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfBookmarkMergerApp(this IServiceCollection services) => services
        .AddSingleton<IUserSettingsService, UserSettingsService>()
        .AddSingleton<FileListViewModel>()
        .AddSingleton<BookmarkTreeViewModel>()
        .AddSingleton<LinkEditorViewModel>()
        .AddSingleton<MainWindowViewModel>();
}
