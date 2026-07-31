using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// ファイル選択画面「上へ」「下へ」の、複数選択時の一括移動・非連続選択時の可否判定を検証する。
/// </summary>
public sealed class FileListViewModelTests
{
    private static (FileListViewModel Sut, List<PdfFileEntryViewModel> Items) CreateSutWithFiles(int count)
    {
        var sut = new FileListViewModel(
            new FakeFileCollectorService(), new FakeMetadataService(), NullLogger<FileListViewModel>.Instance);

        var items = Enumerable.Range(0, count)
            .Select(i => new PdfFileEntryViewModel(new PdfFileEntry { FilePath = $@"C:\pdfs\{i}.pdf" }))
            .ToList();
        foreach (var item in items)
        {
            sut.Files.Add(item);
        }

        return (sut, items);
    }

    [Fact]
    public void MoveSelectionUp_ContiguousMultiSelection_MovesWholeBlockUp_PreservesRelativeOrder()
    {
        var (sut, items) = CreateSutWithFiles(5); // 0,1,2,3,4

        sut.MoveSelectionUp([items[1], items[2], items[3]]);

        sut.Files.Select(f => f.FilePath).ShouldBe([
            @"C:\pdfs\1.pdf", @"C:\pdfs\2.pdf", @"C:\pdfs\3.pdf", @"C:\pdfs\0.pdf", @"C:\pdfs\4.pdf",
        ]);
    }

    [Fact]
    public void MoveSelectionDown_ContiguousMultiSelection_MovesWholeBlockDown_PreservesRelativeOrder()
    {
        var (sut, items) = CreateSutWithFiles(5); // 0,1,2,3,4

        sut.MoveSelectionDown([items[0], items[1], items[2]]);

        sut.Files.Select(f => f.FilePath).ShouldBe([
            @"C:\pdfs\3.pdf", @"C:\pdfs\0.pdf", @"C:\pdfs\1.pdf", @"C:\pdfs\2.pdf", @"C:\pdfs\4.pdf",
        ]);
    }

    [Fact]
    public void MoveSelectionUp_SelectionAlreadyAtTop_DoesNothing()
    {
        var (sut, items) = CreateSutWithFiles(3);

        sut.MoveSelectionUp([items[0], items[1]]);

        sut.Files.ShouldBe(items);
    }

    [Fact]
    public void MoveSelectionDown_SelectionAlreadyAtBottom_DoesNothing()
    {
        var (sut, items) = CreateSutWithFiles(3);

        sut.MoveSelectionDown([items[1], items[2]]);

        sut.Files.ShouldBe(items);
    }

    [Fact]
    public void MoveSelectionUp_NonContiguousSelection_DoesNothing()
    {
        var (sut, items) = CreateSutWithFiles(4); // 0,1,2,3

        sut.MoveSelectionUp([items[1], items[3]]); // 1と3の間に非選択の2があり非連続

        sut.Files.ShouldBe(items);
    }

    [Theory]
    [InlineData(0, 1, false, true)] // 先頭ブロック: 上へ不可・下へ可
    [InlineData(1, 2, true, true)] // 中間ブロック: 両方可
    [InlineData(2, 3, true, false)] // 末尾ブロック: 上へ可・下へ不可
    public void GetMoveAvailability_ContiguousSelection_ReflectsBoundaryPosition(
        int fromIndex, int toIndex, bool expectedCanMoveUp, bool expectedCanMoveDown)
    {
        var (sut, items) = CreateSutWithFiles(4);
        var selected = items[fromIndex..(toIndex + 1)];

        var (canMoveUp, canMoveDown) = sut.GetMoveAvailability(selected);

        canMoveUp.ShouldBe(expectedCanMoveUp);
        canMoveDown.ShouldBe(expectedCanMoveDown);
    }

    [Fact]
    public void GetMoveAvailability_NonContiguousSelection_ReturnsBothFalse()
    {
        var (sut, items) = CreateSutWithFiles(4);

        var (canMoveUp, canMoveDown) = sut.GetMoveAvailability([items[0], items[2]]);

        canMoveUp.ShouldBeFalse();
        canMoveDown.ShouldBeFalse();
    }

    [Fact]
    public void GetMoveAvailability_EmptySelection_ReturnsBothFalse()
    {
        var (sut, _) = CreateSutWithFiles(4);

        var (canMoveUp, canMoveDown) = sut.GetMoveAvailability([]);

        canMoveUp.ShouldBeFalse();
        canMoveDown.ShouldBeFalse();
    }
}
