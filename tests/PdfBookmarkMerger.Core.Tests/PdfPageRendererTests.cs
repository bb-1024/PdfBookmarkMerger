using System.Runtime.Versioning;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("macos")]
public sealed class PdfPageRendererTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfPageRenderer _sut = new();

    public PdfPageRendererTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfPageRendererTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public async Task RenderPageAsync_ReturnsAValidPngWithNonZeroDimensions()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 2);

        var png = await _sut.RenderPageAsync(path, pageIndex: 0, scale: 1.0f);

        png.ShouldNotBeEmpty();
        // PNGシグネチャ(先頭8バイト)を確認する。
        png.Take(8).ShouldBe([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    [Fact]
    public async Task RenderPageAsync_WithLargerScale_ProducesALargerImage()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 1);

        var small = await _sut.RenderPageAsync(path, pageIndex: 0, scale: 1.0f);
        var large = await _sut.RenderPageAsync(path, pageIndex: 0, scale: 2.0f);

        // PNGは可変長圧縮のため直接ピクセル数を比較できないが、解像度が上がればおおむねバイト数も増える。
        large.Length.ShouldBeGreaterThan(small.Length);
    }

    [Fact]
    public async Task GetPageSizeAsync_MatchesTheStandardLetterSizeUsedByPdfSharp()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 1);

        var (width, height) = await _sut.GetPageSizeAsync(path, pageIndex: 0);

        // PDFsharpのAddPage()既定サイズはA4(約595 x 842pt)。
        width.ShouldBe(595, tolerance: 2);
        height.ShouldBe(842, tolerance: 2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
