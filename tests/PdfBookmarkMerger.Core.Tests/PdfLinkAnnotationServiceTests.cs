using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using PdfBookmarkMerger.Core.Tests.TestHelpers;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Shouldly;

namespace PdfBookmarkMerger.Core.Tests;

public sealed class PdfLinkAnnotationServiceTests : IDisposable
{
    private readonly string _workDirectory;
    private readonly PdfLinkAnnotationService _sut = new(NullLogger<PdfLinkAnnotationService>.Instance);

    public PdfLinkAnnotationServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "PdfLinkAnnotationServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDirectory);
    }

    [Fact]
    public async Task ApplyLinksAsync_XyzDestination_WritesRectAndExplicitCoordinates()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 3);

        var link = new LinkAnnotationNode
        {
            GroupId = Guid.NewGuid(),
            SourcePageIndex = 0,
            SourceRect = new PdfRect(Left: 100, Bottom: 700, Right: 200, Top: 720),
            TargetPageIndex = 2,
            DestinationType = BookmarkDestinationType.XYZ,
            Left = 50,
            Top = 750,
            Zoom = 1.5,
        };

        await _sut.ApplyLinksAsync(path, [link]);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        document.Pages[0].Annotations.Count.ShouldBe(1);
        var annotation = document.Pages[0].Annotations[0];
        annotation.Elements.GetName("/Subtype").ShouldBe("/Link");

        var rect = annotation.Elements.GetRectangle("/Rect");
        rect.X1.ShouldBe(100, tolerance: 0.01);
        rect.Y1.ShouldBe(700, tolerance: 0.01);
        rect.X2.ShouldBe(200, tolerance: 0.01);
        rect.Y2.ShouldBe(720, tolerance: 0.01);

        var action = annotation.Elements.GetDictionary("/A");
        action.ShouldNotBeNull();
        action.Elements.GetName("/S").ShouldBe("/GoTo");

        var dest = action.Elements.GetArray("/D");
        dest.ShouldNotBeNull();
        var pageRef = (PdfReference)dest.Elements[0]!;
        pageRef.ObjectID.ShouldBe(document.Pages[2].ReferenceNotNull.ObjectID);
        dest.Elements.GetName(1).ShouldBe("/XYZ");
        dest.Elements.GetReal(2).ShouldBe(50, tolerance: 0.01);
        dest.Elements.GetReal(3).ShouldBe(750, tolerance: 0.01);
        dest.Elements.GetReal(4).ShouldBe(1.5, tolerance: 0.01);
    }

    [Theory]
    [InlineData(BookmarkDestinationType.Fit, "/Fit")]
    [InlineData(BookmarkDestinationType.FitH, "/FitH")]
    [InlineData(BookmarkDestinationType.FitV, "/FitV")]
    public async Task ApplyLinksAsync_NonXyzDestinationTypes_WriteTheCorrectDestinationName(
        BookmarkDestinationType type, string expectedName)
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 2);

        var link = new LinkAnnotationNode
        {
            GroupId = Guid.NewGuid(),
            SourcePageIndex = 0,
            SourceRect = new PdfRect(10, 10, 50, 30),
            TargetPageIndex = 1,
            DestinationType = type,
        };

        await _sut.ApplyLinksAsync(path, [link]);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var action = document.Pages[0].Annotations[0].Elements.GetDictionary("/A")!;
        var dest = action.Elements.GetArray("/D")!;
        dest.Elements.GetName(1).ShouldBe(expectedName);
    }

    [Fact]
    public async Task ApplyLinksAsync_PreservesExistingPageCountAndBookmarks()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithDeepBookmarks(path, pageCount: 5, titlePrefix: "X");

        var link = new LinkAnnotationNode
        {
            GroupId = Guid.NewGuid(),
            SourcePageIndex = 0,
            SourceRect = new PdfRect(0, 0, 10, 10),
            TargetPageIndex = 1,
            DestinationType = BookmarkDestinationType.Fit,
        };

        await _sut.ApplyLinksAsync(path, [link]);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBe(5);
        document.Outlines.Count.ShouldBe(1);
        document.Outlines[0].Title.ShouldBe("X Part 1");
    }

    [Fact]
    public async Task ApplyLinksAsync_MultipleLinksWithTheSameGroupId_AreAllAddedAsSeparateAnnotations()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 3);

        var groupId = Guid.NewGuid();
        var links = new[]
        {
            new LinkAnnotationNode
            {
                GroupId = groupId,
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 700, 100, 720),
                TargetPageIndex = 2,
                DestinationType = BookmarkDestinationType.Fit,
            },
            new LinkAnnotationNode
            {
                GroupId = groupId,
                SourcePageIndex = 0,
                SourceRect = new PdfRect(0, 680, 60, 700),
                TargetPageIndex = 2,
                DestinationType = BookmarkDestinationType.Fit,
            },
        };

        await _sut.ApplyLinksAsync(path, links);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        document.Pages[0].Annotations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyLinksAsync_LinkWithOutOfRangePageIndex_IsSkippedWithoutThrowing()
    {
        var path = Path.Combine(_workDirectory, "a.pdf");
        SamplePdfFactory.CreateWithoutBookmarks(path, pageCount: 2);

        var link = new LinkAnnotationNode
        {
            GroupId = Guid.NewGuid(),
            SourcePageIndex = 0,
            SourceRect = new PdfRect(0, 0, 10, 10),
            TargetPageIndex = 99,
            DestinationType = BookmarkDestinationType.Fit,
        };

        await _sut.ApplyLinksAsync(path, [link]);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        document.Pages[0].HasAnnotations.ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDirectory))
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
    }
}
