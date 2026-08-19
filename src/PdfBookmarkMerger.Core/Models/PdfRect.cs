namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDFユーザー空間(pt単位、左下原点)における矩形。PDFsharpの/Rect配列・PdfPigのGlyphRectangleと
/// 同じ座標系のため、両ライブラリ間の変換なしにそのまま受け渡しできる。
/// </summary>
public readonly record struct PdfRect(double Left, double Bottom, double Right, double Top);
