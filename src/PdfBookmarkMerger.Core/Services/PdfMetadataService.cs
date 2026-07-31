using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfBookmarkMerger.Core.Services;

public sealed class PdfMetadataService(ILogger<PdfMetadataService> logger) : IPdfMetadataService
{
    public Task<int> ReadPageCountAsync(string filePath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }, ct);

    public Task<PdfFileMetadata> ReadMetadataAsync(PdfFileEntry file, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var document = PdfReader.Open(file.FilePath, PdfDocumentOpenMode.Import);

            var pageIndexByPage = BuildPageIndexLookup(document);
            var bookmarks = ExtractOutlines(document.Outlines, pageIndexByPage, file.Id);

            var info = document.Info;
            var properties = new PdfDocumentPropertiesModel
            {
                Title = info.Title ?? string.Empty,
                Author = info.Author ?? string.Empty,
                Subject = info.Subject ?? string.Empty,
                Keywords = info.Keywords ?? string.Empty,
                Creator = info.Creator ?? string.Empty,
            };

            logger.LogInformation(
                "メタデータを読み取りました: {File} ({Pages}ページ, しおり{BookmarkCount}件)",
                file.FileName, document.PageCount, CountAll(bookmarks));

            return new PdfFileMetadata
            {
                FileEntryId = file.Id,
                PageCount = document.PageCount,
                Bookmarks = bookmarks,
                Properties = properties,
            };
        }, ct);

    private List<BookmarkNode> ExtractOutlines(
        PdfOutlineCollection outlines,
        IReadOnlyDictionary<PdfPage, int> pageIndexByPage,
        Guid fileEntryId)
    {
        var result = new List<BookmarkNode>();

        foreach (var outline in outlines)
        {
            var destinationPage = outline.DestinationPage;
            if (destinationPage is null)
            {
                logger.LogWarning(
                    "しおり '{Title}' はドキュメント内ページへのジャンプ先を持たないため読み飛ばします。", outline.Title);
                continue;
            }

            if (!pageIndexByPage.TryGetValue(destinationPage, out var pageIndex))
            {
                logger.LogWarning(
                    "しおり '{Title}' のジャンプ先ページが特定できないため読み飛ばします。", outline.Title);
                continue;
            }

            var node = new BookmarkNode
            {
                SourceFileEntryId = fileEntryId,
                OriginalPageIndex = pageIndex,
                Title = outline.Title ?? string.Empty,
                DestinationType = BookmarkDestinationTypeMapper.FromPdfSharp(outline.PageDestinationType),
                Left = AsFiniteOrNull(outline.Left),
                Top = AsFiniteOrNull(outline.Top),
                Right = AsFiniteOrNull(outline.Right),
                Bottom = AsFiniteOrNull(outline.Bottom),
                Zoom = AsFiniteOrNull(outline.Zoom),
                IsOpen = ReadOpened(outline),
            };

            if (outline.HasChildren)
            {
                node.Children.AddRange(ExtractOutlines(outline.Outlines, pageIndexByPage, fileEntryId));
            }

            result.Add(node);
        }

        return result;
    }

    /// <summary>
    /// PdfSharpのPdfOutline.Left/Top/Right/Bottom/Zoomは、宛先タイプ(/FitH, /FitV等)により
    /// 該当項目が存在しない場合にNaNを返す。NaN/InfinityをそのままBookmarkNodeへ保持すると、
    /// Undoスナップショットのjson化で例外になり、出力PDFへもそのまま書き戻されてしまうため、
    /// 未指定を表すnullに正規化する。
    /// </summary>
    private static double? AsFiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static double? AsFiniteOrNull(double? value) => value is { } d ? AsFiniteOrNull(d) : null;

    /// <summary>
    /// ページオブジェクト(参照比較)からページ番号を引くための辞書を1度だけ構築する。
    /// しおりごとに毎回ページ配列を線形探索(FindPageIndex)していたのを避けるため。
    /// PdfPageは参照比較で十分に一意に特定できる(同一Import内で複製されることはない)。
    /// </summary>
    private static Dictionary<PdfPage, int> BuildPageIndexLookup(PdfDocument document)
    {
        var lookup = new Dictionary<PdfPage, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < document.Pages.Count; i++)
        {
            lookup[document.Pages[i]] = i;
        }

        return lookup;
    }

    /// <summary>
    /// PDFsharp 6.2.4の<see cref="PdfOutline.Opened"/>は、開閉状態を表す/Countの符号を正しく解釈できない
    /// 既知の不具合がある(/Count自体は正しく書き込まれている)。/Countを直接読み取って回避する。
    /// /Countが存在しない場合(葉ノード等)はライブラリの既定値にフォールバックする。
    /// </summary>
    private static bool ReadOpened(PdfOutline outline) =>
        outline.Elements.ContainsKey("/Count")
            ? outline.Elements.GetInteger("/Count") > 0
            : outline.Opened;

    private static int CountAll(IReadOnlyList<BookmarkNode> nodes) =>
        nodes.Sum(n => 1 + CountAll(n.Children));
}
