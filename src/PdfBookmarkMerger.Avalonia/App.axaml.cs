using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfBookmarkMerger.App;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.AvaloniaApp.Services;
using Serilog;

namespace PdfBookmarkMerger.AvaloniaApp;

public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        // Build()実行前(Serilog設定前)に発生した例外も可能な限り拾えるよう、可能な限り早期に登録する。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            PdfBookmarkMergerHostFactory.LogUnhandledException(e.ExceptionObject as Exception, "AppDomain.UnhandledException", flush: e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            PdfBookmarkMergerHostFactory.LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException", flush: false);
            e.SetObserved();
        };
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = PdfBookmarkMergerHostFactory.Build(desktop.Args ?? [], (services, _) =>
            {
                services.AddSingleton<IDialogService, AvaloniaDialogService>();
                services.AddSingleton<MainWindow>();
            });

            _host.Start();

            var userSettings = _host.Services.GetRequiredService<IUserSettingsService>();
            ThemeApplier.Apply(userSettings.Current.ThemeMode);

            desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) =>
            {
                _host.StopAsync().GetAwaiter().GetResult();
                _host.Dispose();
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
