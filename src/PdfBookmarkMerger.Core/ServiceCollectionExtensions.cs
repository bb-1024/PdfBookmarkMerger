using Microsoft.Extensions.DependencyInjection;
using PdfBookmarkMerger.Core.Services;

namespace PdfBookmarkMerger.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfBookmarkMergerCore(this IServiceCollection services) => services
        .AddSingleton<IPdfFileCollectorService, PdfFileCollectorService>()
        .AddSingleton<IPdfMetadataService, PdfMetadataService>()
        .AddSingleton<IPdfMergeService, PdfMergeService>()
        .AddSingleton<IBookmarkSettingsExportService, BookmarkSettingsExportService>();
}
