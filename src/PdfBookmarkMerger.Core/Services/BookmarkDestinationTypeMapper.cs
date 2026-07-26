using PdfBookmarkMerger.Core.Models;
using PdfSharp.Pdf;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// Core.Modelsの<see cref="BookmarkDestinationType"/>とPdfSharpの<see cref="PdfPageDestinationType"/>を相互変換する。
/// Modelsをライブラリ非依存に保つため、変換ロジックはServices層に置く。
/// </summary>
internal static class BookmarkDestinationTypeMapper
{
    public static BookmarkDestinationType FromPdfSharp(PdfPageDestinationType type) => type switch
    {
        PdfPageDestinationType.Xyz => BookmarkDestinationType.XYZ,
        PdfPageDestinationType.Fit => BookmarkDestinationType.Fit,
        PdfPageDestinationType.FitH => BookmarkDestinationType.FitH,
        PdfPageDestinationType.FitV => BookmarkDestinationType.FitV,
        // バウンディングボックス指定(FitB系)・矩形指定(FitR)はUI非対応のため、
        // 対応する非バウンディングボックス版へ簡略化する。
        PdfPageDestinationType.FitBH => BookmarkDestinationType.FitH,
        PdfPageDestinationType.FitBV => BookmarkDestinationType.FitV,
        _ => BookmarkDestinationType.Fit,
    };

    public static PdfPageDestinationType ToPdfSharp(BookmarkDestinationType type) => type switch
    {
        BookmarkDestinationType.XYZ => PdfPageDestinationType.Xyz,
        BookmarkDestinationType.Fit => PdfPageDestinationType.Fit,
        BookmarkDestinationType.FitH => PdfPageDestinationType.FitH,
        BookmarkDestinationType.FitV => PdfPageDestinationType.FitV,
        _ => PdfPageDestinationType.Fit,
    };
}
