using PdfBookmarkMerger.App.Resources;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// BookmarkTreeViewModelのD&D並べ替え・再親子付け(Move)、階層レベル上限の切り詰め(SetChildLevelCapAsync)、
/// 「一律でFitに設定」時のToModel()の非破壊クローン挙動は、UIから手動でしか確認されておらず、
/// 単体テストが1件も存在しなかった。壊れやすいロジックのため、代表的なケースを固定する。
/// </summary>
public sealed class BookmarkTreeViewModelTests
{
    private static readonly Guid FileId = Guid.NewGuid();

    private static BookmarkTreeViewModel CreateSut(out FakeDialogService dialog)
    {
        // Strings.Cultureはプロセス全体で共有される静的状態。他のテスト(StringsTests等)が
        // 英語に切り替えたままになっていないよう、既定(日本語)へ明示的に戻してから使う。
        Strings.Culture = null;

        dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);
        vm.Load([], new Dictionary<Guid, string> { [FileId] = "sample.pdf" }, [FileId]);
        return vm;
    }

    [Fact]
    public void Move_WithinSameParent_ReordersRootNodesAndUnderlyingModel()
    {
        var vm = CreateSut(out _);
        var a = vm.AddRoot();
        a.Title.Value = "A";
        var b = vm.AddRoot();
        b.Title.Value = "B";
        var c = vm.AddRoot();
        c.Title.Value = "C";

        // Aを末尾(Cの後ろ)へ移動する。
        vm.Move(a, null, vm.RootNodes.Count);

        vm.RootNodes.Select(n => n.Title.Value).ShouldBe(["B", "C", "A"]);
        vm.ToModel().Select(n => n.Title).ShouldBe(["B", "C", "A"]);
    }

    [Fact]
    public void Move_AcrossDifferentParents_UpdatesBothOldAndNewParentModelChildren()
    {
        var vm = CreateSut(out _);
        var parent1 = vm.AddRoot();
        parent1.Title.Value = "Parent1";
        var child = vm.AddChild(parent1);
        child.Title.Value = "Child";
        var parent2 = vm.AddRoot();
        parent2.Title.Value = "Parent2";

        vm.Move(child, parent2, 0);

        parent1.Children.ShouldBeEmpty();
        parent1.Model.Children.ShouldBeEmpty();
        parent2.Children.Select(n => n.Title.Value).ShouldBe(["Child"]);
        parent2.Model.Children.Select(n => n.Title).ShouldBe(["Child"]);
        child.Parent.ShouldBe(parent2);
    }

    [Fact]
    public void Move_OntoOwnDescendant_IsNoOp()
    {
        var vm = CreateSut(out _);
        var parent = vm.AddRoot();
        var child = vm.AddChild(parent);

        vm.Move(parent, child, 0);

        vm.RootNodes.ShouldContain(parent);
        parent.Children.ShouldContain(child);
    }

    [Fact]
    public async Task SetChildLevelCapAsync_TruncatesDescendantsBelowSelectedLevel_InBothViewAndModel()
    {
        var vm = CreateSut(out var dialog);
        var root = vm.AddRoot();
        var child = vm.AddChild(root);
        var grandchild = vm.AddChild(child);
        vm.AddChild(grandchild);

        // root=Level1, child=Level2, grandchild=Level3, great-grandchild=Level4。
        // Level2までを残す(=childの子であるgrandchild以下を削除する)指定にする。
        dialog.LevelCapDialogResult = root.LevelNumber + 1;

        await vm.SetChildLevelCapAsync(root);

        child.Children.ShouldBeEmpty();
        child.Model.Children.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetChildLevelCapAsync_OffersNodesOwnLevelAsTheMinimumSelectableOption()
    {
        var vm = CreateSut(out var dialog);
        var root = vm.AddRoot();
        vm.AddChild(root);
        dialog.LevelCapDialogResult = root.LevelNumber;

        await vm.SetChildLevelCapAsync(root);

        dialog.LastLevelCapDialogRange.ShouldBe((root.LevelNumber, root.LevelNumber + 1));
    }

    [Fact]
    public async Task SetChildLevelCapAsync_SelectingNodesOwnLevel_RemovesAllChildren()
    {
        var vm = CreateSut(out var dialog);
        var root = vm.AddRoot();
        var child = vm.AddChild(root);
        vm.AddChild(child);
        dialog.LevelCapDialogResult = root.LevelNumber;

        await vm.SetChildLevelCapAsync(root);

        root.Children.ShouldBeEmpty();
        root.Model.Children.ShouldBeEmpty();
    }

    [Fact]
    public void PromoteLevel_ChildNode_BecomesSiblingImmediatelyAfterOldParent()
    {
        var vm = CreateSut(out _);
        var parent = vm.AddRoot();
        parent.Title.Value = "Parent";
        var child = vm.AddChild(parent);
        child.Title.Value = "Child";
        var nextRoot = vm.AddRoot();
        nextRoot.Title.Value = "NextRoot";

        vm.PromoteLevel(child);

        vm.RootNodes.Select(n => n.Title.Value).ShouldBe(["Parent", "Child", "NextRoot"]);
        parent.Children.ShouldBeEmpty();
        child.Parent.ShouldBeNull();
    }

    [Fact]
    public void PromoteLevel_RootNode_IsNoOp()
    {
        var vm = CreateSut(out _);
        var root = vm.AddRoot();

        vm.PromoteLevel(root);

        vm.RootNodes.ShouldContain(root);
        vm.CanPromoteLevel(root).ShouldBeFalse();
    }

    [Fact]
    public void DemoteLevel_SecondRootNode_BecomesLastChildOfPrecedingSibling()
    {
        var vm = CreateSut(out _);
        var first = vm.AddRoot();
        first.Title.Value = "First";
        var second = vm.AddRoot();
        second.Title.Value = "Second";

        vm.DemoteLevel(second);

        vm.RootNodes.Select(n => n.Title.Value).ShouldBe(["First"]);
        first.Children.Select(n => n.Title.Value).ShouldBe(["Second"]);
        second.Parent.ShouldBe(first);
    }

    [Fact]
    public void DemoteLevel_FirstNodeWithNoPrecedingSibling_IsNoOp()
    {
        var vm = CreateSut(out _);
        var only = vm.AddRoot();

        vm.DemoteLevel(only);

        vm.RootNodes.ShouldContain(only);
        vm.CanDemoteLevel(only).ShouldBeFalse();
    }

    [Fact]
    public void CanUndo_WithNoEdits_IsFalse()
    {
        var vm = CreateSut(out _);

        vm.CanUndo.Value.ShouldBeFalse();
    }

    [Fact]
    public void Undo_AfterAddRoot_RemovesTheAddedNodeAndDisablesFurtherUndo()
    {
        var vm = CreateSut(out _);
        vm.AddRoot();
        vm.CanUndo.Value.ShouldBeTrue();

        vm.UndoCommand.Execute();

        vm.RootNodes.ShouldBeEmpty();
        vm.CanUndo.Value.ShouldBeFalse();
    }

    [Fact]
    public void Undo_AfterRemove_RestoresTheRemovedNode()
    {
        var vm = CreateSut(out _);
        var node = vm.AddRoot();
        vm.Remove(node);
        vm.RootNodes.ShouldBeEmpty();

        vm.UndoCommand.Execute();

        vm.RootNodes.Count.ShouldBe(1);
        vm.RootNodes[0].Title.Value.ShouldBe("新しいしおり");
    }

    [Fact]
    public void Undo_AfterMove_RestoresThePreviousParentAndPosition()
    {
        var vm = CreateSut(out _);
        var parent1 = vm.AddRoot();
        var child = vm.AddChild(parent1);
        var parent2 = vm.AddRoot();
        vm.Move(child, parent2, 0);

        vm.UndoCommand.Execute();

        // Move前はRootNodes=[parent1(childを持つ), parent2]の順だった。
        vm.RootNodes.Count.ShouldBe(2);
        vm.RootNodes[0].Children.ShouldHaveSingleItem();
        vm.RootNodes[1].Children.ShouldBeEmpty();
    }

    [Fact]
    public void Undo_MultipleTimes_RevertsEachAddInReverseOrder()
    {
        var vm = CreateSut(out _);
        vm.AddRoot();
        vm.AddRoot();
        vm.RootNodes.Count.ShouldBe(2);

        vm.UndoCommand.Execute();
        vm.RootNodes.Count.ShouldBe(1);

        vm.UndoCommand.Execute();
        vm.RootNodes.ShouldBeEmpty();
        vm.CanUndo.Value.ShouldBeFalse();
    }

    [Fact]
    public void Undo_WithNoHistory_IsNoOp()
    {
        var vm = CreateSut(out _);
        vm.AddRoot();

        vm.UndoCommand.Execute();
        // これ以上戻せない状態でもう一度実行しても例外を投げず、状態はそのまま。
        vm.UndoCommand.Execute();

        vm.RootNodes.ShouldBeEmpty();
        vm.CanUndo.Value.ShouldBeFalse();
    }

    [Fact]
    public void RapidSuccessiveEditsToSameNodeProperty_CoalesceIntoASingleUndoStep()
    {
        var vm = CreateSut(out _);
        var node = vm.AddRoot();

        // 短時間内の連続したTitle変更(テキスト入力を想定)は、まとめて1回の編集として扱われる。
        node.Title.Value = "A";
        node.Title.Value = "AB";
        node.Title.Value = "ABC";

        vm.UndoCommand.Execute();
        vm.RootNodes.Count.ShouldBe(1);
        vm.RootNodes[0].Title.Value.ShouldBe("新しいしおり");

        vm.UndoCommand.Execute();
        vm.RootNodes.ShouldBeEmpty();
    }

    [Fact]
    public void EditsToDifferentProperties_AreNotCoalescedTogether()
    {
        var vm = CreateSut(out _);
        var node = vm.AddRoot();

        node.Title.Value = "Changed Title";
        node.IsOpen.Value = true;

        vm.UndoCommand.Execute();
        vm.RootNodes[0].Title.Value.ShouldBe("Changed Title");
        vm.RootNodes[0].IsOpen.Value.ShouldBeFalse();

        vm.UndoCommand.Execute();
        vm.RootNodes[0].Title.Value.ShouldBe("新しいしおり");
    }

    [Fact]
    public void ToModel_WhenForceFitForAll_ReturnsNonDestructiveCloneWithoutStaleCoordinates()
    {
        var vm = CreateSut(out _);
        var node = vm.AddRoot();
        node.DestinationType.Value = BookmarkDestinationType.XYZ;
        node.Top.Value = 700;
        node.Left.Value = 50;
        node.Zoom.Value = 1.5;

        vm.ForceFitForAll.Value = true;
        var forMerge = vm.ToModel();

        forMerge.Count.ShouldBe(1);
        forMerge[0].DestinationType.ShouldBe(BookmarkDestinationType.Fit);
        forMerge[0].Top.ShouldBeNull();
        forMerge[0].Left.ShouldBeNull();
        forMerge[0].Zoom.ShouldBeNull();

        // 元データ(座標値)は破壊されておらず、オフに戻すと編集画面上も復元される。
        vm.ForceFitForAll.Value = false;
        node.DestinationType.Value.ShouldBe(BookmarkDestinationType.XYZ);
        node.Top.Value.ShouldBe(700);
        node.Left.Value.ShouldBe(50);
        node.Zoom.Value.ShouldBe(1.5);
    }

    private static readonly Guid FileIdB = Guid.NewGuid();

    /// <summary>
    /// FileId(結合前ページ1・5・9、MergedPageIndex 0・4・8=1番目のファイル)と、
    /// FileIdB(結合前ページ1、MergedPageIndex 10=FileIdの後に10ページとして結合される2番目のファイル)を
    /// 持つツリーを構築する。結合前ページ数編集の同一ファイル内波及・ファイル横断的な結合後ページ数の
    /// 連鎖の検証に使う。
    /// </summary>
    private static (BookmarkTreeViewModel Vm, BookmarkNodeViewModel Page1, BookmarkNodeViewModel Page5, BookmarkNodeViewModel Page9, BookmarkNodeViewModel OtherFilePage1)
        CreateSutWithTwoFiles()
    {
        Strings.Culture = null;
        var dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);

        var page1 = new BookmarkNode { SourceFileEntryId = FileId, Title = "Page1", OriginalPageIndex = 0, MergedPageIndex = 0 };
        var page5 = new BookmarkNode { SourceFileEntryId = FileId, Title = "Page5", OriginalPageIndex = 4, MergedPageIndex = 4 };
        var page9 = new BookmarkNode { SourceFileEntryId = FileId, Title = "Page9", OriginalPageIndex = 8, MergedPageIndex = 8 };
        var otherFilePage1 = new BookmarkNode { SourceFileEntryId = FileIdB, Title = "OtherPage1", OriginalPageIndex = 0, MergedPageIndex = 10 };

        vm.Load([page1, page5, page9, otherFilePage1], new Dictionary<Guid, string>
        {
            [FileId] = "a.pdf",
            [FileIdB] = "b.pdf",
        }, [FileId, FileIdB]);

        var page1Vm = vm.RootNodes.Single(n => n.Title.Value == "Page1");
        var page5Vm = vm.RootNodes.Single(n => n.Title.Value == "Page5");
        var page9Vm = vm.RootNodes.Single(n => n.Title.Value == "Page9");
        var otherVm = vm.RootNodes.Single(n => n.Title.Value == "OtherPage1");

        return (vm, page1Vm, page5Vm, page9Vm, otherVm);
    }

    [Fact]
    public void EditingPreOffsetPageNumber_ShiftsSameFileNodesFromThatOriginalPageOnward_ButNotEarlierOnes()
    {
        var (vm, page1, page5, page9, otherFilePage1) = CreateSutWithTwoFiles();

        // Page5(結合前ページ5)を8に変更(delta=+3)。
        page5.PreOffsetPageNumber.Value = 8;

        page1.PreOffsetPageNumber.Value.ShouldBe(1, "手前のしおりは変更されない");
        page5.PreOffsetPageNumber.Value.ShouldBe(8);
        page9.PreOffsetPageNumber.Value.ShouldBe(12, "後続のしおりは同じ差分だけ一律で変更される");
    }

    [Fact]
    public void EditingPreOffsetPageNumber_ShiftsDisplayMergedPageNumber_ForSameFileAndSubsequentFiles()
    {
        var (vm, page1, page5, page9, otherFilePage1) = CreateSutWithTwoFiles();

        page5.PreOffsetPageNumber.Value = 8;

        page1.DisplayMergedPageNumber.Value.ShouldBe(1, "手前のしおりの結合後ページ数は変わらない");
        page5.DisplayMergedPageNumber.Value.ShouldBe(8, "結合後ページ数(5)+差分(3)");
        page9.DisplayMergedPageNumber.Value.ShouldBe(12, "結合後ページ数(9)+差分(3)");
        otherFilePage1.DisplayMergedPageNumber.Value.ShouldBe(14, "後続ファイルの結合後ページ数(11)+差分(3)");
    }

    [Fact]
    public void EditingPreOffsetPageNumber_SetsHasPageNumberEdits_AndClearingItBackReturnsToFalse()
    {
        var (vm, _, page5, _, _) = CreateSutWithTwoFiles();

        vm.HasPageNumberEdits.Value.ShouldBeFalse();

        page5.PreOffsetPageNumber.Value = 8;
        vm.HasPageNumberEdits.Value.ShouldBeTrue();

        // 5に戻す(差分が正味ゼロに戻る)。
        page5.PreOffsetPageNumber.Value = 5;
        vm.HasPageNumberEdits.Value.ShouldBeFalse();
    }

    [Fact]
    public void EditingPreOffsetPageNumber_ToZeroOrLess_SetsHasPageNumberInconsistency()
    {
        var (vm, page1, _, _, _) = CreateSutWithTwoFiles();

        page1.PreOffsetPageNumber.Value = 0;

        vm.HasPageNumberInconsistency.Value.ShouldBeTrue();
    }

    [Fact]
    public void EditingPreOffsetPageNumber_WithoutInconsistency_DoesNotSetHasPageNumberInconsistency()
    {
        var (vm, _, page5, _, _) = CreateSutWithTwoFiles();

        page5.PreOffsetPageNumber.Value = 8;

        vm.HasPageNumberInconsistency.Value.ShouldBeFalse();
    }

    [Fact]
    public void Undo_AfterEditingPreOffsetPageNumber_RestoresOriginalPageNumbers()
    {
        var (vm, _, page5, page9, _) = CreateSutWithTwoFiles();

        page5.PreOffsetPageNumber.Value = 8;
        vm.CanUndo.Value.ShouldBeTrue();

        vm.UndoCommand.Execute();

        var restoredPage5 = vm.RootNodes.Single(n => n.Title.Value == "Page5");
        var restoredPage9 = vm.RootNodes.Single(n => n.Title.Value == "Page9");
        restoredPage5.PreOffsetPageNumber.Value.ShouldBe(5);
        restoredPage9.PreOffsetPageNumber.Value.ShouldBe(9);
        vm.HasPageNumberEdits.Value.ShouldBeFalse();
    }

    [Fact]
    public void ToExportModel_BakesInSameFileAndCrossFileOffset_IntoPageOffset()
    {
        var (vm, page1, page5, page9, otherFilePage1) = CreateSutWithTwoFiles();

        page5.PreOffsetPageNumber.Value = 8;

        var exported = vm.ToExportModel();
        var exportedPage1 = exported.Single(n => n.Title == "Page1");
        var exportedPage5 = exported.Single(n => n.Title == "Page5");
        var exportedPage9 = exported.Single(n => n.Title == "Page9");
        var exportedOther = exported.Single(n => n.Title == "OtherPage1");

        exportedPage1.PageOffset.GetValueOrDefault().ShouldBe(0);
        exportedPage5.PageOffset.ShouldBe(3);
        exportedPage9.PageOffset.ShouldBe(3);
        exportedOther.PageOffset.ShouldBe(3, "後続ファイルには自分自身の編集が無くても連鎖分が加算される");
    }
}
