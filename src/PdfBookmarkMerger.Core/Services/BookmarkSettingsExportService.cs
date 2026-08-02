using System.Globalization;
using System.Text;
using System.Xml;
using PdfBookmarkMerger.Core.Models;

namespace PdfBookmarkMerger.Core.Services;

/// <summary>
/// しおりツリーを「しおり設定ファイル仕様」(UTF-8のXML、ルート&lt;Bookmark&gt;直下に&lt;Title&gt;を
/// 入れ子で並べる形式)に従って書き出す。PageタグはPDF Referenceの表示方法(Fit/FitH/FitV/XYZ)と
/// 同じ引数順で出力し、値が未設定(null)の引数は0を代用する(仕様上、Zoom=0はnullと同義とされており、
/// 本実装ではLeft/Topにも同じ規約を適用して引数の位置ずれを避ける)。
/// </summary>
public sealed class BookmarkSettingsExportService : IBookmarkSettingsExportService
{
    public Task ExportAsync(IReadOnlyList<BookmarkNode> bookmarks, string outputPath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var textWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 仕様書の例と厳密に一致させるため、宣言行は手書きする
            // (XmlWriterのWriteStartDocumentはEncoding名を小文字"utf-8"で出力するため)。
            textWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
            textWriter.Flush();

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                Async = false,
            };

            using (var xmlWriter = XmlWriter.Create(textWriter, settings))
            {
                xmlWriter.WriteStartElement("Bookmark");
                foreach (var node in bookmarks)
                {
                    ct.ThrowIfCancellationRequested();
                    WriteTitle(xmlWriter, node, ct);
                }

                xmlWriter.WriteEndElement();
            }
        }, ct);

    private static void WriteTitle(XmlWriter writer, BookmarkNode node, CancellationToken ct)
    {
        writer.WriteStartElement("Title");
        writer.WriteAttributeString("Page", BuildPageAttribute(node));
        writer.WriteAttributeString("Action", node.ActionType);
        writer.WriteString(node.Title);

        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            WriteTitle(writer, child, ct);
        }

        writer.WriteEndElement();
    }

    private static string BuildPageAttribute(BookmarkNode node)
    {
        var pageNumber = (node.MergedPageIndex ?? node.OriginalPageIndex) + 1;
        var mode = node.DestinationType switch
        {
            BookmarkDestinationType.Fit => "Fit",
            BookmarkDestinationType.FitH => $"FitH {FormatNumber(node.Top)}",
            BookmarkDestinationType.FitV => $"FitV {FormatNumber(node.Left)}",
            BookmarkDestinationType.XYZ =>
                $"XYZ {FormatNumber(node.Left)} {FormatNumber(node.Top)} {FormatNumber(node.Zoom)}",
            _ => "Fit",
        };

        return $"{pageNumber} {mode}";
    }

    private static string FormatNumber(double? value) => (value ?? 0).ToString("0.####", CultureInfo.InvariantCulture);
}
