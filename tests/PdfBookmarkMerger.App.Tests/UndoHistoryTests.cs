using PdfBookmarkMerger.App.Undo;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

public sealed class UndoHistoryTests
{
    [Fact]
    public void TryPop_EmptyHistory_ReturnsFalse()
    {
        var sut = new UndoHistory<string>();

        sut.TryPop(out _).ShouldBeFalse();
        sut.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Push_ThenTryPop_ReturnsMostRecentSnapshotFirst_LastInFirstOut()
    {
        var sut = new UndoHistory<string>();
        sut.Push("state-1", sizeBytes: 10);
        sut.Push("state-2", sizeBytes: 10);
        sut.Push("state-3", sizeBytes: 10);

        sut.TryPop(out var first).ShouldBeTrue();
        first.ShouldBe("state-3");
        sut.TryPop(out var second).ShouldBeTrue();
        second.ShouldBe("state-2");
        sut.TryPop(out var third).ShouldBeTrue();
        third.ShouldBe("state-1");
        sut.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Push_ExceedingMaxTotalBytes_EvictsOldestEntriesFirst()
    {
        var sut = new UndoHistory<string>(maxTotalBytes: 25);
        sut.Push("oldest", sizeBytes: 10);
        sut.Push("middle", sizeBytes: 10);
        sut.Push("newest", sizeBytes: 10); // 合計30 > 25なので、最古の"oldest"が破棄される想定。

        sut.TryPop(out var a).ShouldBeTrue();
        a.ShouldBe("newest");
        sut.TryPop(out var b).ShouldBeTrue();
        b.ShouldBe("middle");
        sut.TryPop(out _).ShouldBeFalse(); // "oldest"は上限超過により既に破棄されている。
    }

    [Fact]
    public void Push_SingleEntryLargerThanMax_IsStillKept_SoUndoRemainsUsable()
    {
        var sut = new UndoHistory<string>(maxTotalBytes: 5);
        sut.Push("huge-single-entry", sizeBytes: 1000);

        sut.CanUndo.ShouldBeTrue();
        sut.TryPop(out var snapshot).ShouldBeTrue();
        snapshot.ShouldBe("huge-single-entry");
    }

    [Fact]
    public void Clear_RemovesAllHistory()
    {
        var sut = new UndoHistory<string>();
        sut.Push("state-1", sizeBytes: 10);

        sut.Clear();

        sut.CanUndo.ShouldBeFalse();
        sut.TryPop(out _).ShouldBeFalse();
    }
}
