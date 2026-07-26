using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfBookmarkMerger.App;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.WpfApp.Services;
using Serilog;

namespace PdfBookmarkMerger.WpfApp;

public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        // Build()実行前(Serilog設定前)に発生した例外も可能な限り拾えるよう、可能な限り早期に登録する。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            PdfBookmarkMergerHostFactory.LogUnhandledException(e.ExceptionObject as Exception, "AppDomain.UnhandledException", flush: e.IsTerminating);

        DispatcherUnhandledException += (_, e) =>
            PdfBookmarkMergerHostFactory.LogUnhandledException(e.Exception, "Dispatcher.UnhandledException", flush: true);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            PdfBookmarkMergerHostFactory.LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException", flush: false);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = PdfBookmarkMergerHostFactory.Build(e.Args, (services, _) =>
        {
            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<MainWindow>();
        });

        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var userSettings = _host.Services.GetRequiredService<IUserSettingsService>();

        // ユーザー設定の表示モード(ライト/ダーク/システム設定)を起動時に反映する。
        ThemeApplier.Apply(mainWindow, userSettings.Current.ThemeMode);

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
