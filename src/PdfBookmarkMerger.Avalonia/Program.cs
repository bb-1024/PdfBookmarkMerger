using Avalonia;
using System;
using PdfBookmarkMerger.App;

namespace PdfBookmarkMerger.AvaloniaApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // AvaloniaにはWPFのDispatcherUnhandledExceptionに相当する、UIスレッド専用の
            // 未処理例外フックが無い(Avalonia.Base.dllに該当メンバーが存在しないことを確認済み)。
            // メインループ全体(StartWithClassicDesktopLifetime)をここで囲むことで、イベント
            // ハンドラ等から投げられUIスレッドの外まで伝播した例外も、無言でクラッシュせず
            // 確実にログへ残るようにする。
            PdfBookmarkMergerHostFactory.LogUnhandledException(ex, "Avalonia.MainLoop", flush: true);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
