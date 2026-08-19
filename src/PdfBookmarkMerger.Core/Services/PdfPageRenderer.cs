using PDFtoImage;
using SkiaSharp;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PDFtoImage(PDFiumラッパー)でページを描画する。呼び出しごとにファイル全体を読み込む
/// ステートレスな実装だが、実測(2000ページのPDFで16〜26ms/ページ、ページ位置による劣化なし)より、
/// 文書ハンドルを保持する最適化は不要と判断した。
/// PDFiumはスレッドセーフでないため、呼び出しは直列化する(同時に複数ページを描画しない)。
///
/// PDFtoImageのAPIはWindows/macOS/Linux等、このアプリの対応OS全てを含む形でサポート対象を宣言して
/// いるが、[SupportedOSPlatform]をこのクラスやCoreアセンブリ全体に付けると、Core内の無関係な型
/// (BookmarkNode等)まで実質的にOS制限が伝播し、App層の既存コード全体がCA1416警告まみれになる
/// (実際に試して確認した)。実際にはこの2箇所のPDFtoImage呼び出しだけが対象のため、
/// ここでのみ局所的に警告を抑制する。
/// </summary>
public sealed class PdfPageRenderer : IPdfPageRenderer
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<byte[]> RenderPageAsync(string filePath, int pageIndex, float scale, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
#pragma warning disable CA1416 // このアプリの対応OS(Windows/macOS)はPDFtoImageのサポート対象に含まれる。
            var options = new RenderOptions(Dpi: (int)(96 * scale), WithAnnotations: false);
            using var bitmap = Conversion.ToImage(bytes, page: pageIndex, options: options);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
            return encoded.ToArray();
#pragma warning restore CA1416
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(double Width, double Height)> GetPageSizeAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
#pragma warning disable CA1416 // このアプリの対応OS(Windows/macOS)はPDFtoImageのサポート対象に含まれる。
            var size = Conversion.GetPageSize(bytes, page: pageIndex);
            return (size.Width, size.Height);
#pragma warning restore CA1416
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
