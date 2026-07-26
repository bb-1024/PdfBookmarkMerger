using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfFileCollectorServiceTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfFileCollectorService _sut = new(NullLogger<PdfFileCollectorService>.Instance);

    public PdfFileCollectorServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfBookmarkMergerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public void ExpandToPdfFilePaths_ExpandsFolder_TopLevelOnly()
    {
        var topLevelPdf = Path.Combine(_workDirectory, "top.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(topLevelPdf, pageCount: 1);

        var nonPdf = Path.Combine(_workDirectory, "readme.txt");
        File.WriteAllText(nonPdf, "not a pdf");

        var subDirectory = Path.Combine(_workDirectory, "sub");
        Directory.CreateDirectory(subDirectory);
        var nestedPdf = Path.Combine(subDirectory, "nested.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(nestedPdf, pageCount: 1);

        var result = _sut.ExpandToPdfFilePaths([_workDirectory]);

        result.Count.ShouldBe(1);
        result[0].ShouldBe(Path.GetFullPath(topLevelPdf));
    }

    [Fact]
    public void ExpandToPdfFilePaths_MixOfFilesAndFolders_ReturnsAllPdfsInGivenOrder()
    {
        var directFile = Path.Combine(_workDirectory, "direct.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(directFile, pageCount: 1);

        var folder = Path.Combine(_workDirectory, "folder");
        Directory.CreateDirectory(folder);
        var inFolder = Path.Combine(folder, "in-folder.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(inFolder, pageCount: 1);

        var result = _sut.ExpandToPdfFilePaths([directFile, folder]);

        result.Count.ShouldBe(2);
        result.ShouldContain(Path.GetFullPath(directFile));
        result.ShouldContain(Path.GetFullPath(inFolder));
    }

    [Fact]
    public void ExpandToPdfFilePaths_IgnoresMissingPaths()
    {
        var result = _sut.ExpandToPdfFilePaths([Path.Combine(_workDirectory, "does-not-exist.pdf")]);

        result.ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
