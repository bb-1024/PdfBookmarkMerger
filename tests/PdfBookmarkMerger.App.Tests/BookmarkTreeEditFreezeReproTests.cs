using Microsoft.Extensions.Logging.Abstractions;
using PdfBookmarkMerger.App.Tests.TestHelpers;
using PdfBookmarkMerger.App.ViewModels;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;
using Shouldly;

namespace PdfBookmarkMerger.App.Tests;

/// <summary>
/// ユーザー報告(D&amp;D・レベル上下・子要素のレベル上限設定でフリーズする)の再現テスト。
/// これまでのBookmarkTreeViewModelTestsは、いずれもAddRoot/AddChildで作った単純な合成ツリーに対する
/// 検証だったため、実際にPDFから抽出・複数ファイル結合したツリー特有の形状に起因する不具合を
/// 見逃していた可能性がある。tests/sample配下の実サンプルPDFを、本番と同じCoreサービス
/// (PdfFileCollectorService → PdfMetadataService → MissingBookmarkFallback → BookmarkOffsetCalculator)
/// で読み込み、実際にウィンドウを介さずに各編集操作を実行して、フリーズ(タイムアウト)しないことを検証する。
/// </summary>
public sealed class BookmarkTreeEditFreezeReproTests
{
    private static readonly string SampleDirectory = FindSampleDirectory();

    [Fact]
    public async Task Move_OnRealMultiFileMergedTree_CompletesWithoutHanging()
    {
        var (vm, _) = await LoadRealSampleTreeAsync();

        var root = vm.RootNodes[0];
        var secondRoot = vm.RootNodes[1];

        var task = Task.Run(() => vm.Move(root, secondRoot, 0));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;

        completed.ShouldBeTrue("BookmarkTreeViewModel.Move did not complete within 10 seconds on a real, multi-file-merged tree.");
        await task; // 例外があれば再throwして表面化させる。
    }

    [Fact]
    public async Task PromoteLevel_OnRealMultiFileMergedTree_CompletesWithoutHanging()
    {
        var (vm, _) = await LoadRealSampleTreeAsync();

        var target = FindDeepestNode(vm.RootNodes);

        var task = Task.Run(() => vm.PromoteLevel(target));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;

        completed.ShouldBeTrue("BookmarkTreeViewModel.PromoteLevel did not complete within 10 seconds on a real, multi-file-merged tree.");
        await task;
    }

    [Fact]
    public async Task DemoteLevel_OnRealMultiFileMergedTree_CompletesWithoutHanging()
    {
        var (vm, _) = await LoadRealSampleTreeAsync();

        var target = vm.RootNodes[^1];

        var task = Task.Run(() => vm.DemoteLevel(target));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;

        completed.ShouldBeTrue("BookmarkTreeViewModel.DemoteLevel did not complete within 10 seconds on a real, multi-file-merged tree.");
        await task;
    }

    [Fact]
    public async Task SetChildLevelCapAsync_OnRealMultiFileMergedTree_CompletesWithoutHanging()
    {
        var (vm, dialog) = await LoadRealSampleTreeAsync();
        var root = vm.RootNodes[0];
        dialog.LevelCapDialogResult = root.LevelNumber;

        var task = vm.SetChildLevelCapAsync(root);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;

        completed.ShouldBeTrue("BookmarkTreeViewModel.SetChildLevelCapAsync did not complete within 10 seconds on a real, multi-file-merged tree.");
        await task;
    }

    [Fact]
    public async Task Move_ThenPromoteThenDemote_RepeatedSequenceOnRealTree_NeverHangs()
    {
        // 単発ではなく、実際のUI操作のように複数の編集(D&D→レベル上げ→レベル下げ)を連続で行う。
        var (vm, _) = await LoadRealSampleTreeAsync();

        var task = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                var node = FindDeepestNode(vm.RootNodes);
                if (vm.CanPromoteLevel(node))
                {
                    vm.PromoteLevel(node);
                }

                var again = FindDeepestNode(vm.RootNodes);
                if (vm.CanDemoteLevel(again))
                {
                    vm.DemoteLevel(again);
                }

                if (vm.RootNodes.Count > 1)
                {
                    vm.Move(vm.RootNodes[0], vm.RootNodes[^1], 0);
                }
            }
        });

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20))) == task;

        completed.ShouldBeTrue("A repeated sequence of Move/PromoteLevel/DemoteLevel hung on a real tree.");
        await task;
    }

    private static BookmarkNodeViewModel FindDeepestNode(IReadOnlyList<BookmarkNodeViewModel> nodes)
    {
        BookmarkNodeViewModel? deepest = null;
        var deepestLevel = -1;

        void Walk(IEnumerable<BookmarkNodeViewModel> level)
        {
            foreach (var node in level)
            {
                if (node.LevelNumber > deepestLevel)
                {
                    deepestLevel = node.LevelNumber;
                    deepest = node;
                }

                Walk(node.Children);
            }
        }

        Walk(nodes);
        return deepest ?? throw new InvalidOperationException("Tree has no nodes.");
    }

    private static async Task<(BookmarkTreeViewModel Tree, FakeDialogService Dialog)> LoadRealSampleTreeAsync()
    {
        var collector = new PdfFileCollectorService(NullLogger<PdfFileCollectorService>.Instance);
        var metadataService = new PdfMetadataService(NullLogger<PdfMetadataService>.Instance);

        var paths = collector.ExpandToPdfFilePaths([SampleDirectory]);
        paths.ShouldNotBeEmpty("No sample PDFs found under " + SampleDirectory);

        var files = paths.Select(p => new PdfFileEntry { FilePath = p }).ToList();
        var metadataByFileId = new Dictionary<Guid, PdfFileMetadata>();
        foreach (var file in files)
        {
            metadataByFileId[file.Id] = await metadataService.ReadMetadataAsync(file);
        }

        var effectiveBookmarks = MissingBookmarkFallback.ResolveEffectiveBookmarks(files, metadataByFileId);
        var merged = BookmarkOffsetCalculator.ComputeMergedBookmarks(files, effectiveBookmarks, metadataByFileId);
        var fileNames = files.ToDictionary(f => f.Id, f => f.FileName);

        var dialog = new FakeDialogService();
        var vm = new BookmarkTreeViewModel(dialog);
        vm.Load(merged, fileNames, files.Select(f => f.Id).ToList());

        vm.RootNodes.ShouldNotBeEmpty("Real sample tree loaded with zero root bookmarks.");

        return (vm, dialog);
    }

    private static string FindSampleDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "sample");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tests/sample by walking up from " + AppContext.BaseDirectory);
    }
}
