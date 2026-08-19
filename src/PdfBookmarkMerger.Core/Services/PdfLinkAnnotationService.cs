using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfBookmarkMerger.Core.Services;

public sealed class PdfLinkAnnotationService(ILogger<PdfLinkAnnotationService> logger) : IPdfLinkAnnotationService
{
    public Task ApplyLinksAsync(string filePath, IReadOnlyList<LinkAnnotationNode> links, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // Modifyモードで開き、ページを一切コピーし直さず(AddPageを使わず)、既存のPdfPageへ
            // 直接/Annotsを追記する。AddPage経由でページを再構築すると、結合直後にPdfMergeService.
            // RemapLinkDestinationsで修正したのと同種の「内部リンクのジャンプ先が壊れる」不具合を
            // 既存のしおり・既存リンクに対して再度引き起こしうるため、既存構造には一切触れない。
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify);

            foreach (var link in links)
            {
                ct.ThrowIfCancellationRequested();

                if (link.SourcePageIndex < 0 || link.SourcePageIndex >= document.PageCount ||
                    link.TargetPageIndex < 0 || link.TargetPageIndex >= document.PageCount)
                {
                    logger.LogWarning(
                        "リンク(Id={LinkId})のページ番号が範囲外のため追加をスキップします。", link.Id);
                    continue;
                }

                AddLinkAnnotation(document, link);
            }

            document.Save(filePath);

            logger.LogInformation("リンク注釈を追加しました: {FilePath} ({Count}件)", filePath, links.Count);
        }, ct);

    private static void AddLinkAnnotation(PdfDocument document, LinkAnnotationNode link)
    {
        var sourcePage = document.Pages[link.SourcePageIndex];
        var targetPage = document.Pages[link.TargetPageIndex];

        var destArray = BuildDestinationArray(document, targetPage, link);

        var action = new PdfDictionary(document);
        action.Elements.SetName("/S", "/GoTo");
        action.Elements.SetValue("/D", destArray);
        document.Internals.AddObject(action);

        var rect = link.SourceRect;
        var linkDict = new PdfDictionary(document);
        linkDict.Elements.SetName("/Type", "/Annot");
        linkDict.Elements.SetName("/Subtype", "/Link");
        linkDict.Elements.SetRectangle(
            "/Rect",
            new PdfRectangle(new XRect(rect.Left, rect.Bottom, rect.Right - rect.Left, rect.Top - rect.Bottom)));
        linkDict.Elements.SetReference("/A", action);
        document.Internals.AddObject(linkDict);

        if (!sourcePage.Elements.ContainsKey("/Annots"))
        {
            sourcePage.Elements.SetValue("/Annots", new PdfArray(document));
        }

        var annots = sourcePage.Elements.GetArray("/Annots")!;
        annots.Elements.Add(linkDict.ReferenceNotNull);
    }

    /// <summary>
    /// PDF仕様のジャンプ先配列を表示方法(DestinationType)ごとに組み立てる。
    /// 未指定(null)の座標はPDFの/D配列上でも「変更なし」を表すnullトークンとして書き込む。
    /// </summary>
    private static PdfArray BuildDestinationArray(PdfDocument document, PdfPage targetPage, LinkAnnotationNode link)
    {
        var array = new PdfArray(document);
        array.Elements.Add(targetPage.ReferenceNotNull);

        switch (link.DestinationType)
        {
            case BookmarkDestinationType.Fit:
                array.Elements.Add(new PdfName("/Fit"));
                break;
            case BookmarkDestinationType.FitH:
                array.Elements.Add(new PdfName("/FitH"));
                array.Elements.Add(ToItem(link.Top));
                break;
            case BookmarkDestinationType.FitV:
                array.Elements.Add(new PdfName("/FitV"));
                array.Elements.Add(ToItem(link.Left));
                break;
            case BookmarkDestinationType.XYZ:
            default:
                array.Elements.Add(new PdfName("/XYZ"));
                array.Elements.Add(ToItem(link.Left));
                array.Elements.Add(ToItem(link.Top));
                array.Elements.Add(ToItem(link.Zoom));
                break;
        }

        return array;
    }

    private static PdfItem ToItem(double? value) => value is { } d ? new PdfReal(d) : PdfNull.Value;
}
