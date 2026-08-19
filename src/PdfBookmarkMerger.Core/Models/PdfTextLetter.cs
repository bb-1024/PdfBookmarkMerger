namespace PdfBookmarkMerger.Core.Models;

/// <summary>PDFページ上の1文字(グリフ)とその矩形(PDFユーザー空間、pt)。</summary>
public readonly record struct PdfTextLetter(string Value, PdfRect Rect);
