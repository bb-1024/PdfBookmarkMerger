using PdfBookmarkMerger.Core.Models;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 結合後PDF保存時のプロパティ編集ダイアログのViewModel。
/// 初期値は先頭PDFファイルのプロパティを流用する。
/// </summary>
public sealed class PropertiesDialogViewModel : ViewModelBase
{
    public PropertiesDialogViewModel(PdfDocumentPropertiesModel initial)
    {
        Title = new ReactivePropertySlim<string>(initial.Title).AddTo(Disposables);
        Author = new ReactivePropertySlim<string>(initial.Author).AddTo(Disposables);
        Subject = new ReactivePropertySlim<string>(initial.Subject).AddTo(Disposables);
        Keywords = new ReactivePropertySlim<string>(initial.Keywords).AddTo(Disposables);
        Creator = new ReactivePropertySlim<string>(initial.Creator).AddTo(Disposables);
    }

    public ReactivePropertySlim<string> Title { get; }

    public ReactivePropertySlim<string> Author { get; }

    public ReactivePropertySlim<string> Subject { get; }

    public ReactivePropertySlim<string> Keywords { get; }

    public ReactivePropertySlim<string> Creator { get; }

    public PdfDocumentPropertiesModel ToModel() => new()
    {
        Title = Title.Value,
        Author = Author.Value,
        Subject = Subject.Value,
        Keywords = Keywords.Value,
        Creator = Creator.Value,
    };
}
