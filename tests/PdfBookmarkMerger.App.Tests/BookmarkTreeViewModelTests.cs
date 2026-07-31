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
        vm.Load([], new Dictionary<Guid, string> { [FileId] = "sample.pdf" });
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
}
