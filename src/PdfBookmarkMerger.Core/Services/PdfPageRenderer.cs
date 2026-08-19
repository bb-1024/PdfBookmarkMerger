using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PDFtoImage(PDFiumラッパー)でページを描画する。呼び出しごとにファイル全体を読み込む
/// ステートレスな実装だが、実測(2000ページのPDFで16〜26ms/ページ、ページ位置による劣化なし)より、
/// 文書ハンドルを保持する最適化は不要と判断した。
/// PDFiumはスレッドセーフでないため、呼び出しは直列化する(同時に複数ページを描画しない)。
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("macos")]
public sealed class PdfPageRenderer : IPdfPageRenderer
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<byte[]> RenderPageAsync(string filePath, int pageIndex, float scale, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var options = new RenderOptions(Dpi: (int)(96 * scale), WithAnnotations: false);
            using var bitmap = Conversion.ToImage(bytes, page: pageIndex, options: options);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
            return encoded.ToArray();
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
            var size = Conversion.GetPageSize(bytes, page: pageIndex);
            return (size.Width, size.Height);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
