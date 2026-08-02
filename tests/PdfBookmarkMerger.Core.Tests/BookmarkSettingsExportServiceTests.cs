using System.Xml.Linq;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

/// <summary>
/// BookmarkSettingsExportServiceが「しおり設定ファイル仕様」(UTF-8のXML、ルート&lt;Bookmark&gt;直下に
/// &lt;Title Page="..." Action="GoTo"&gt;を入れ子で並べる形式)通りに書き出すことを検証する。
/// </summary>
public sealed class BookmarkSettingsExportServiceTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly BookmarkSettingsExportService _sut = new();
    private static readonly Guid FileId = Guid.NewGuid();

    public BookmarkSettingsExportServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }

    private static BookmarkNode CreateNode(string title, int originalPageIndex, int? mergedPageIndex = null,
        BookmarkDestinationType destinationType = BookmarkDestinationType.Fit,
        double? left = null, double? top = null, double? zoom = null) => new()
    {
        SourceFileEntryId = FileId,
        Title = title,
        OriginalPageIndex = originalPageIndex,
        MergedPageIndex = mergedPageIndex ?? originalPageIndex,
        DestinationType = destinationType,
        Left = left,
        Top = top,
        Zoom = zoom,
    };

    private async Task<XDocument> ExportAndReadAsync(IReadOnlyList<BookmarkNode> bookmarks)
    {
        var outputPath = Path.Combine(_workDirectory, "out.xml");
        await _sut.ExportAsync(bookmarks, outputPath);
        return XDocument.Load(outputPath);
    }

    [Fact]
    public async Task ExportAsync_WritesDeclarationLine_ExactlyAsSpecified()
    {
        var outputPath = Path.Combine(_workDirectory, "out.xml");
        await _sut.ExportAsync([CreateNode("表題1", 0)], outputPath);

        var firstLine = (await File.ReadAllLinesAsync(outputPath))[0];
        firstLine.ShouldBe("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    }

    [Fact]
    public async Task ExportAsync_FlatRootTitles_ProducePageAndActionAttributes()
    {
        var doc = await ExportAndReadAsync([
            CreateNode("表題1", originalPageIndex: 0),
            CreateNode("表題2", originalPageIndex: 8),
        ]);

        doc.Root!.Name.LocalName.ShouldBe("Bookmark");
        var titles = doc.Root.Elements("Title").ToList();
        titles.Count.ShouldBe(2);

        titles[0].Attribute("Page")!.Value.ShouldBe("1 Fit");
        titles[0].Attribute("Action")!.Value.ShouldBe("GoTo");
        titles[0].Value.ShouldBe("表題1");

        titles[1].Attribute("Page")!.Value.ShouldBe("9 Fit");
    }

    [Fact]
    public async Task ExportAsync_NestedChildren_ProduceNestedTitleElements()
    {
        var parent = CreateNode("グループ1", originalPageIndex: 2);
        parent.Children.Add(CreateNode("項目1", originalPageIndex: 4));

        var doc = await ExportAndReadAsync([parent]);

        var parentElement = doc.Root!.Element("Title")!;
        parentElement.Attribute("Page")!.Value.ShouldBe("3 Fit");

        var childElement = parentElement.Element("Title")!;
        childElement.Attribute("Page")!.Value.ShouldBe("5 Fit");
        childElement.Value.ShouldBe("項目1");
    }

    [Theory]
    [InlineData(BookmarkDestinationType.FitH, "FitH 100")]
    [InlineData(BookmarkDestinationType.FitV, "FitV 200")]
    public async Task ExportAsync_FitHAndFitV_IncludeSingleCoordinateParameter(BookmarkDestinationType type, string expectedMode)
    {
        var node = CreateNode("t", originalPageIndex: 0, destinationType: type, left: 200, top: 100);

        var doc = await ExportAndReadAsync([node]);

        doc.Root!.Element("Title")!.Attribute("Page")!.Value.ShouldBe($"1 {expectedMode}");
    }

    [Fact]
    public async Task ExportAsync_Xyz_IncludesLeftTopZoomInOrder()
    {
        var node = CreateNode("t", originalPageIndex: 0, destinationType: BookmarkDestinationType.XYZ, left: 10, top: 20, zoom: 1.5);

        var doc = await ExportAndReadAsync([node]);

        doc.Root!.Element("Title")!.Attribute("Page")!.Value.ShouldBe("1 XYZ 10 20 1.5");
    }

    [Fact]
    public async Task ExportAsync_XyzWithoutCoordinates_SubstitutesZeroToKeepArgumentPositions()
    {
        var node = CreateNode("t", originalPageIndex: 0, destinationType: BookmarkDestinationType.XYZ);

        var doc = await ExportAndReadAsync([node]);

        doc.Root!.Element("Title")!.Attribute("Page")!.Value.ShouldBe("1 XYZ 0 0 0");
    }

    [Fact]
    public async Task ExportAsync_UsesMergedPageIndex_FallingBackToOriginalPageIndexWhenAbsent()
    {
        var withMerged = CreateNode("t", originalPageIndex: 4, mergedPageIndex: 24);
        var withoutMerged = new BookmarkNode
        {
            SourceFileEntryId = FileId,
            Title = "u",
            OriginalPageIndex = 2,
            MergedPageIndex = null,
        };

        var doc = await ExportAndReadAsync([withMerged, withoutMerged]);

        var titles = doc.Root!.Elements("Title").ToList();
        titles[0].Attribute("Page")!.Value.ShouldBe("25 Fit");
        titles[1].Attribute("Page")!.Value.ShouldBe("3 Fit");
    }
}
