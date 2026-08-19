using PdfBookmarkMerger.Core.Models;
using UglyToad.PdfPig;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// PdfPigで文字単位のテキストと位置を抽出する。PdfPigのGlyphRectangleは
/// PDFsharpの/Rect配列と同じ座標系(PDFユーザー空間、pt、左下原点)のため、変換なしで扱える。
/// </summary>
public sealed class PdfTextExtractor : IPdfTextExtractor
{
    public Task<IReadOnlyList<PdfTextLetter>> ExtractLettersAsync(string filePath, int pageIndex, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var document = PdfDocument.Open(filePath);
            var page = document.GetPage(pageIndex + 1); // PdfPigは1始まり

            var letters = new List<PdfTextLetter>(page.Letters.Count);
            foreach (var letter in page.Letters)
            {
                var r = letter.GlyphRectangle;
                letters.Add(new PdfTextLetter(letter.Value, new PdfRect(r.Left, r.Bottom, r.Right, r.Top)));
            }

            return (IReadOnlyList<PdfTextLetter>)letters;
        }, ct);
}
