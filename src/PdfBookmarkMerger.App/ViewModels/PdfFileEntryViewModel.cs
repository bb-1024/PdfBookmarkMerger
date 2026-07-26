using PdfBookmarkMerger.Core.Models;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 結合対象PDFファイル一覧の1行分のViewModel。
/// </summary>
public sealed class PdfFileEntryViewModel : ViewModelBase
{
    public PdfFileEntryViewModel(PdfFileEntry model)
    {
        Model = model;
        PageCount = new ReactivePropertySlim<int?>(model.PageCount).AddTo(Disposables);
        LoadFailed = new ReactivePropertySlim<bool>(false).AddTo(Disposables);
    }

    public PdfFileEntry Model { get; }

    public Guid Id => Model.Id;

    public string FilePath => Model.FilePath;

    public string FileName => Model.FileName;

    public ReactivePropertySlim<int?> PageCount { get; }

    /// <summary>
    /// しおり編集(ConfirmFilesAsync)の段階でメタデータ読み込みに失敗したファイルであることを示す。
    /// trueのファイルはしおりツリーだけでなく、実際の結合(MergeAsync)の対象からも除外しなければならない。
    /// </summary>
    public ReactivePropertySlim<bool> LoadFailed { get; }

    public void ApplyPageCount(int pageCount)
    {
        Model.PageCount = pageCount;
        PageCount.Value = pageCount;
    }

    public void MarkLoadFailed()
    {
        LoadFailed.Value = true;
    }
}
