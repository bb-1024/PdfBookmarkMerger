using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.Core;
using Serilog;

namespace PdfBookmarkMerger.App;

/// <summary>
/// WPF版・Avalonia版の両方から呼び出す、汎用ホスト(Generic Host)の共通組み立て処理。
/// Configuration(appsettings.json + ユーザー設定ファイル) / Options / Serilog / DIの配線をここに集約する。
/// </summary>
public static class PdfBookmarkMergerHostFactory
{
    /// <param name="configureUiServices">UIフレームワーク固有のサービス(ウィンドウ・ダイアログ等)を登録するコールバック。</param>
    public static IHost Build(string[] args, Action<IServiceCollection, IConfiguration> configureUiServices)
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);

        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(AppPaths.UserSettingsFilePath, optional: true, reloadOnChange: true);

        var loggerConfiguration = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "pdfbookmarkmerger-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14);

#if DEBUG
        // コンソールへの出力は開発時(dotnet run等でコンソールにアタッチされている場合)の
        // 確認用。配布版(Release)には通常アタッチされたコンソールが無く不要なため、Debugのみ有効にする。
        loggerConfiguration.WriteTo.Console();
#endif

        Log.Logger = loggerConfiguration.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(dispose: true);

        builder.Services.Configure<PdfBookmarkMergerOptions>(builder.Configuration.GetSection(PdfBookmarkMergerOptions.SectionName));

        builder.Services.AddPdfBookmarkMergerCore();
        builder.Services.AddPdfBookmarkMergerApp();

        configureUiServices(builder.Services, builder.Configuration);

        return builder.Build();
    }

    /// <summary>
    /// WPF/Avaloniaそれぞれのグローバル未処理例外ハンドラ(AppDomain.UnhandledException、
    /// DispatcherUnhandledException等)から呼び出す。ハンドラ未登録のままだと、アプリが
    /// 無言でクラッシュしログに何も残らず、事後の障害調査ができなくなるため設けている。
    /// <paramref name="flush"/>は、このあとプロセスが終了する(≒これ以上ログを書く機会がない)場合にtrue。
    /// 継続実行されるハンドラ(未観測タスク例外等)でtrueにすると、以降のログが一切書き込まれなくなるため注意。
    /// </summary>
    public static void LogUnhandledException(Exception? exception, string source, bool flush)
    {
        Log.Logger.Fatal(exception, "未処理の例外が発生しました({Source})。", source);
        if (flush)
        {
            Log.CloseAndFlush();
        }
    }
}
