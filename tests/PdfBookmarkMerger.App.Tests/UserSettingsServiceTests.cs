using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Services;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// UserSettingsService.SaveAsync(内部でawait File.WriteAllTextAsyncを使う)が、
/// AppLanguageBootstrapper.ApplyAsync(...).GetAwaiter().GetResult()のように、UIスレッドを
/// 同期的にブロックした状態から呼ばれても正しく完了する(デッドロックしない)ことを検証する。
///
/// 実際に発生した不具合: SaveAsync内部のawaitにConfigureAwait(false)が無かったため、
/// 継続処理が(ブロックされて塞がっている)呼び出し元のSynchronizationContextへの復帰を試み、
/// 初回起動時(Language未設定 → AppLanguageBootstrapperがSaveAsyncを呼ぶ)にアプリが
/// ウィンドウを一切表示せず無反応のまま固まった。WPF/AvaloniaのUIスレッドは「ブロックされている
/// スレッド自身がディスパッチループも兼ねる」ため、投稿された継続処理を誰も消化できず永久に停止する。
/// </summary>
public sealed class UserSettingsServiceTests : IDisposable
{
    // AppPaths.AppDataDirectoryは実ユーザーのAppDataフォルダを指すため、環境変数で一時フォルダに
    // 差し替えてから使う。差し替えないと、このテストが実際のsettings.jsonを上書き・削除してしまう。
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());

    public UserSettingsServiceTests()
    {
        Environment.SetEnvironmentVariable("PDFBOOKMARKMERGER_APPDATA_DIR", _tempDirectory);
    }

    [Fact]
    public void SaveAsync_CalledSynchronouslyUnderABlockedUiLikeSynchronizationContext_DoesNotDeadlock()
    {
        var service = new UserSettingsService(new FakeOptionsMonitor(), NullLogger<UserSettingsService>.Instance);
        var completed = false;

        // WPF/AvaloniaのUIスレッドを模した専用スレッド上で、投稿された継続処理を一切消化しない
        // SynchronizationContextを設定したうえで、GetAwaiter().GetResult()で同期的にブロックする。
        // これは実際の不具合発生時の構造(App.OnStartup内での呼び出し)を忠実に再現する。
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            service.SaveAsync(new PdfBookmarkMergerOptions { Language = AppLanguage.Japanese })
                .GetAwaiter().GetResult();
            completed = true;
        });
        thread.Start();

        var joinedInTime = thread.Join(TimeSpan.FromSeconds(5));

        joinedInTime.ShouldBeTrue(
            "SaveAsyncが5秒以内に完了しませんでした(デッドロック再発の疑い)。" +
            "File.WriteAllTextAsyncへのawaitにConfigureAwait(false)が付いているか確認してください。");
        completed.ShouldBeTrue();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PDFBOOKMARKMERGER_APPDATA_DIR", null);

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>投稿(Post)されたコールバックを記録するだけで一切実行しない、意図的に「ポンプしない」SynchronizationContext。</summary>
    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // 意図的に何もしない。
        }
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<PdfBookmarkMergerOptions>
    {
        public PdfBookmarkMergerOptions CurrentValue { get; } = new();

        public PdfBookmarkMergerOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<PdfBookmarkMergerOptions, string> listener) => new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
