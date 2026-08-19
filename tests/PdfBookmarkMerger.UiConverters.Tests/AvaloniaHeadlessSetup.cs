using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;

namespace PdfBookmarkMerger.UiConverters.Tests;

/// <summary>
/// Avalonia.Media.Imaging.Bitmapの生成にはプラットフォームレンダリング基盤(IPlatformRenderInterface)の
/// 初期化が必要(通常はアプリ起動時にAppBuilder経由で行われる)。テストプロセスにはそれが無いため、
/// テストアセンブリ読み込み時に一度だけヘッドレスプラットフォームとして初期化する。
/// </summary>
internal static class AvaloniaHeadlessSetup
{
    [ModuleInitializer]
    internal static void Initialize() =>
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
}
