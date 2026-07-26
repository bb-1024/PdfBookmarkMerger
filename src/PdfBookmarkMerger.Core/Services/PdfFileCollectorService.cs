using Microsoft.Extensions.Logging;

namespace PdfBookmarkMerger.Core.Services;

public sealed class PdfFileCollectorService(ILogger<PdfFileCollectorService> logger) : IPdfFileCollectorService
{
    public IReadOnlyList<string> ExpandToPdfFilePaths(IEnumerable<string> droppedPaths)
    {
        var result = new List<string>();

        foreach (var path in droppedPaths)
        {
            if (File.Exists(path))
            {
                if (IsPdf(path))
                {
                    result.Add(Path.GetFullPath(path));
                }
                else
                {
                    logger.LogInformation("PDF以外のファイルを無視しました: {Path}", path);
                }
            }
            else if (Directory.Exists(path))
            {
                // 子フォルダは検索対象外(直下のみ)。
                var pdfFiles = Directory.EnumerateFiles(path, "*.pdf", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                result.AddRange(pdfFiles.Select(Path.GetFullPath));
            }
            else
            {
                logger.LogWarning("存在しないパスを無視しました: {Path}", path);
            }
        }

        return result;
    }

    private static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
}
