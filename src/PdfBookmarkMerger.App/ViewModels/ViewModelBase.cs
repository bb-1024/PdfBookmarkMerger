using System.Reactive.Disposables;

namespace PdfBookmarkMerger.App.ViewModels;

public abstract class ViewModelBase : IDisposable
{
    protected CompositeDisposable Disposables { get; } = [];

    public void Dispose()
    {
        Disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
