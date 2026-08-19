using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
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

    public Task<IReadOnlyList<LinkAnnotationNode>> ReadExistingLinksAsync(string filePath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            var pageIndexByObjectId = BuildPageIndexByObjectId(document);
            var result = new List<LinkAnnotationNode>();

            for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var page = document.Pages[pageIndex];
                if (!page.Elements.ContainsKey("/Annots"))
                {
                    continue;
                }

                var annots = page.Elements.GetArray("/Annots");
                if (annots is null)
                {
                    continue;
                }

                for (var i = 0; i < annots.Elements.Count; i++)
                {
                    if (ResolveDictionary(annots.Elements[i]) is not { } annotDict ||
                        annotDict.Elements.GetName("/Subtype") != "/Link")
                    {
                        continue;
                    }

                    if (TryReadLink(annotDict, pageIndex, pageIndexByObjectId) is { } link)
                    {
                        result.Add(link);
                    }
                }
            }

            logger.LogInformation("既存のリンク注釈を読み取りました: {FilePath} ({Count}件)", filePath, result.Count);
            return (IReadOnlyList<LinkAnnotationNode>)result;
        }, ct);

    /// <summary>
    /// ページオブジェクトの識別子(ObjectID)からページ番号を引くための辞書を1度だけ構築する
    /// (/Dで参照される間接オブジェクトの解決に使う)。
    /// </summary>
    private static Dictionary<PdfObjectID, int> BuildPageIndexByObjectId(PdfDocument document)
    {
        var lookup = new Dictionary<PdfObjectID, int>();
        for (var i = 0; i < document.PageCount; i++)
        {
            lookup[document.Pages[i].ReferenceNotNull.ObjectID] = i;
        }

        return lookup;
    }

    private static PdfDictionary? ResolveDictionary(PdfItem item) => item switch
    {
        PdfDictionary dict => dict,
        PdfReference { Value: PdfDictionary dict } => dict,
        _ => null,
    };

    /// <summary>
    /// 1件のLink注釈から、ドキュメント内ページへのGoToジャンプ先を読み取る。
    /// 外部URL(/URIアクション)など、ドキュメント内ページへのジャンプ先を持たないものはnullを返す。
    /// </summary>
    private static LinkAnnotationNode? TryReadLink(PdfDictionary annotDict, int sourcePageIndex, IReadOnlyDictionary<PdfObjectID, int> pageIndexByObjectId)
    {
        var destArray = ResolveDestinationArray(annotDict);
        if (destArray is null || destArray.Elements.Count == 0 ||
            destArray.Elements[0] is not PdfReference targetPageRef ||
            !pageIndexByObjectId.TryGetValue(targetPageRef.ObjectID, out var targetPageIndex))
        {
            return null;
        }

        var rect = annotDict.Elements.GetRectangle("/Rect");
        var sourceRect = new PdfRect(Left: rect.X1, Bottom: rect.Y1, Right: rect.X2, Top: rect.Y2);

        var destinationTypeName = destArray.Elements.Count > 1 ? destArray.Elements.GetName(1) : "/XYZ";
        var (destinationType, left, top, right, bottom, zoom) = destinationTypeName switch
        {
            "/Fit" or "/FitB" => (BookmarkDestinationType.Fit, (double?)null, (double?)null, (double?)null, (double?)null, (double?)null),
            "/FitH" or "/FitBH" => (BookmarkDestinationType.FitH, null, GetNullableReal(destArray, 2), null, null, null),
            "/FitV" or "/FitBV" => (BookmarkDestinationType.FitV, GetNullableReal(destArray, 2), null, null, null, null),
            _ => (BookmarkDestinationType.XYZ, GetNullableReal(destArray, 2), GetNullableReal(destArray, 3), (double?)null, (double?)null, GetNullableReal(destArray, 4)),
        };

        return new LinkAnnotationNode
        {
            GroupId = Guid.NewGuid(),
            SourcePageIndex = sourcePageIndex,
            SourceRect = sourceRect,
            TargetPageIndex = targetPageIndex,
            DestinationType = destinationType,
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
            Zoom = zoom,
        };
    }

    /// <summary>
    /// /A(/S /GoTo /D [...])経由・/Dest直接指定のいずれの形式にも対応してジャンプ先配列を取り出す。
    /// </summary>
    private static PdfArray? ResolveDestinationArray(PdfDictionary annotDict)
    {
        if (annotDict.Elements.GetDictionary("/A") is { } action && action.Elements.GetName("/S") == "/GoTo")
        {
            return action.Elements.GetArray("/D");
        }

        return annotDict.Elements.GetArray("/Dest");
    }

    /// <summary>PdfLinkAnnotationService自身がToItemで書き込むnullトークン(PdfNull)を、
    /// 「未指定」としてnullへ正しく戻す(GetRealは数値以外だと例外または既定値になり判別できないため)。</summary>
    private static double? GetNullableReal(PdfArray array, int index)
    {
        if (index >= array.Elements.Count)
        {
            return null;
        }

        return array.Elements[index] switch
        {
            PdfReal real => real.Value,
            PdfInteger integer => integer.Value,
            _ => null,
        };
    }

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

        // /Borderを省略するとPDF仕様上の既定値[0 0 1](幅1の枠線)が使われ、ビューワによっては
        // リンク範囲に矩形の枠線が表示されてしまう。ホットスポットは可視化せず透明に振る舞わせたいため、
        // 明示的に幅0を指定する。
        var border = new PdfArray(document);
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        linkDict.Elements.SetValue("/Border", border);

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
