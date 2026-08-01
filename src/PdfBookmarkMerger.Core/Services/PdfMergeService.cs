using Microsoft.Extensions.Logging;
using PdfBookmarkMerger.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfBookmarkMerger.Core.Services;

public sealed class PdfMergeService(ILogger<PdfMergeService> logger) : IPdfMergeService
{
    /// <summary>
    /// 入力ファイルを開く(ディスクI/O・PDF構造解析)処理を並列化する際の最大同時実行数。
    /// CPUコア数に連動させつつ、大量ファイル時のスレッドプール枯渇・ファイルハンドル過多を避けるため上限を設ける。
    /// </summary>
    private static readonly int MaxParallelOpen = Math.Clamp(Environment.ProcessorCount, 1, 8);

    public Task MergeAsync(PdfMergeRequest request, IProgress<MergeProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var totalFileCount = request.Files.Count;

            // フェーズ1で一部のファイルだけ開けた状態(パスワード保護・破損・他プロセスによるロック等で
            // 途中のファイルがPdfReader.Openに失敗、またはキャンセルされた)でも、既に開けた分は
            // 必ずDisposeする(ファイルハンドルがプロセス終了までロックされたままになるのを防ぐ)ため、
            // フェーズ1・フェーズ2の両方を同じtry/finallyで囲む。
            var opened = new PdfDocument?[totalFileCount];
            try
            {
                // フェーズ1: 各入力PDFを開く(ディスクI/O・構造解析、ファイルごとに独立)処理を並列化する。
                // 出力側(PdfDocument output)への書き込みはスレッドセーフではないため、ここでは行わない。
                using (var semaphore = new SemaphoreSlim(MaxParallelOpen))
                {
                    var openTasks = request.Files.Select(async (file, index) =>
                    {
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            opened[index] = PdfReader.Open(file.FilePath, PdfDocumentOpenMode.Import);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(openTasks).ConfigureAwait(false);
                }

                using var output = new PdfDocument();
                var pageMap = new Dictionary<(Guid FileId, int OriginalPageIndex), PdfPage>();

                // フェーズ2: 開いた各PDFのページを出力へ追加する(単一スレッド、高速なメモリ内コピーが中心)。
                for (var index = 0; index < totalFileCount; index++)
                {
                    ct.ThrowIfCancellationRequested();

                    var file = request.Files[index];
                    var input = opened[index]!;

                    for (var i = 0; i < input.PageCount; i++)
                    {
                        var addedPage = output.AddPage(input.Pages[i]);
                        pageMap[(file.Id, i)] = addedPage;
                    }

                    logger.LogInformation("結合対象に追加: {File} ({Pages}ページ)", file.FileName, input.PageCount);
                    progress?.Report(new MergeProgress(index + 1, totalFileCount, file.FileName));
                }

                ApplyBookmarks(output.Outlines, request.Bookmarks, pageMap);

                output.Info.Title = request.Properties.Title;
                output.Info.Author = request.Properties.Author;
                output.Info.Subject = request.Properties.Subject;
                output.Info.Keywords = request.Properties.Keywords;
                output.Info.Creator = request.Properties.Creator;

                var directory = Path.GetDirectoryName(request.OutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var totalPageCount = output.PageCount;
                output.Save(request.OutputPath);

                logger.LogInformation("PDF結合完了: {OutputPath} (全{Pages}ページ)", request.OutputPath, totalPageCount);
            }
            finally
            {
                foreach (var input in opened)
                {
                    input?.Dispose();
                }
            }
        }, ct);

    private void ApplyBookmarks(
        PdfOutlineCollection target,
        IReadOnlyList<BookmarkNode> nodes,
        IReadOnlyDictionary<(Guid FileId, int OriginalPageIndex), PdfPage> pageMap)
    {
        foreach (var node in nodes)
        {
            if (!pageMap.TryGetValue((node.SourceFileEntryId, node.OriginalPageIndex), out var page))
            {
                logger.LogWarning("しおり '{Title}' のジャンプ先ページが結合結果内に見つからないため読み飛ばします。", node.Title);
                continue;
            }

            var outline = target.Add(node.Title, page, node.IsOpen);
            outline.PageDestinationType = BookmarkDestinationTypeMapper.ToPdfSharp(node.DestinationType);

            if (node.Left is { } left)
            {
                outline.Left = left;
            }

            if (node.Top is { } top)
            {
                outline.Top = top;
            }

            if (node.Right is { } right)
            {
                outline.Right = right;
            }

            if (node.Bottom is { } bottom)
            {
                outline.Bottom = bottom;
            }

            if (node.Zoom is { } zoom)
            {
                outline.Zoom = zoom;
            }

            if (node.Children.Count > 0)
            {
                ApplyBookmarks(outline.Outlines, node.Children, pageMap);

                // PDFsharp 6.2.4は、document.Outlinesの直下(第1階層)以外のしおりについて
                // 開閉状態を表す/Countを書き込まない既知の不具合がある。保存前に直接設定して回避する。
                var childCount = node.Children.Count;
                outline.Elements.SetInteger("/Count", node.IsOpen ? childCount : -childCount);
            }
        }
    }
}
