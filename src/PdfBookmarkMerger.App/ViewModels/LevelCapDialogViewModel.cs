using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 「子要素のレベル上限を設定」ダイアログのViewModel。minLevel~maxLevel(ルートから数えた絶対レベル、
/// しおり編集ツリーの表示と対応する)の範囲から上限レベルを選択する。既定値はmaxLevel(=何も削除されない、安全な初期値)。
/// </summary>
public sealed class LevelCapDialogViewModel : ViewModelBase
{
    public LevelCapDialogViewModel(int minLevel, int maxLevel)
    {
        MinLevel = minLevel;
        MaxLevel = maxLevel;
        AvailableLevels = Enumerable.Range(minLevel, maxLevel - minLevel + 1).ToList();
        SelectedLevel = new ReactivePropertySlim<int>(maxLevel).AddTo(Disposables);
    }

    public int MinLevel { get; }

    public int MaxLevel { get; }

    public IReadOnlyList<int> AvailableLevels { get; }

    public ReactivePropertySlim<int> SelectedLevel { get; }
}
