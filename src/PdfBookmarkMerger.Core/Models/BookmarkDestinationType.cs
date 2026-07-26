namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDFしおり(Outline)のジャンプ先表示方法。PDF仕様のGoToアクションにおける表示方法に対応する。
/// UIで選択可能なのはこの4種類のみとする(FitR/FitB系のバウンディングボックス指定は非対応とし、
/// 読み込み時はFit/FitH/FitVへ簡略化する)。
/// </summary>
public enum BookmarkDestinationType
{
    XYZ,
    Fit,
    FitH,
    FitV,
}
