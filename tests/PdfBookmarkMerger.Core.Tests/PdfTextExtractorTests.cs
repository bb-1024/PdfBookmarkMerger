using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfTextExtractorTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfTextExtractor _sut = new();

    public PdfTextExtractorTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfTextExtractorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public async Task ExtractLettersAsync_ReturnsOneLetterPerCharacter_InReadingOrder()
    {
        var path = Path.Combine(_workDirectory, "text.pdf");
        SamplePdfFactory.CreateWithText(path, "Hi PDF");

        var letters = await _sut.ExtractLettersAsync(path, pageIndex: 0);

        // "Hi PDF"の空白を除いた文字がそのまま読み順で得られること。
        var chars = string.Concat(letters.Select(l => l.Value));
        chars.ShouldBe("Hi PDF");
    }

    [Fact]
    public async Task ExtractLettersAsync_LetterRectangles_AreInAscendingXOrder_AndWithinTheStandardLetterPage()
    {
        var path = Path.Combine(_workDirectory, "text.pdf");
        SamplePdfFactory.CreateWithText(path, "ABC");

        var letters = await _sut.ExtractLettersAsync(path, pageIndex: 0);

        letters.Count.ShouldBe(3);
        // 左から右に並んでいる(各文字のLeftが単調増加)ことを確認する。
        for (var i = 1; i < letters.Count; i++)
        {
            letters[i].Rect.Left.ShouldBeGreaterThan(letters[i - 1].Rect.Left);
        }

        // レターサイズ(612 x 792pt)のページ内に収まっていること。
        foreach (var letter in letters)
        {
            letter.Rect.Left.ShouldBeInRange(0, 612);
            letter.Rect.Right.ShouldBeInRange(0, 612);
            letter.Rect.Bottom.ShouldBeInRange(0, 792);
            letter.Rect.Top.ShouldBeInRange(0, 792);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
