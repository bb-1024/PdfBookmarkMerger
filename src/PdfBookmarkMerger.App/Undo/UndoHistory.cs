namespace PdfBookmarkMerger.App.Undo;

/// <summary>
/// スナップショット方式のUndo履歴。各スナップショットの推定サイズ(バイト)を積算し、
/// 合計が上限(既定100MB)を超えたら、最新の1件を除き最も古い履歴から順に破棄する。
/// 保持件数を固定値で制限するのではなく、実際に消費するメモリ量を基準に自律的に決定する。
/// </summary>
public sealed class UndoHistory<T>
{
    public const long DefaultMaxTotalBytes = 100 * 1024 * 1024;

    private readonly long _maxTotalBytes;
    private readonly List<(T Snapshot, long SizeBytes)> _entries = [];
    private long _totalBytes;

    public UndoHistory(long maxTotalBytes = DefaultMaxTotalBytes)
    {
        _maxTotalBytes = maxTotalBytes;
    }

    public bool CanUndo => _entries.Count > 0;

    public void Push(T snapshot, long sizeBytes)
    {
        _entries.Add((snapshot, sizeBytes));
        _totalBytes += sizeBytes;

        // 最新の1件は、それ単体で上限を超えていても保持する(そうしないとUndoが常に機能しなくなる)。
        while (_totalBytes > _maxTotalBytes && _entries.Count > 1)
        {
            _totalBytes -= _entries[0].SizeBytes;
            _entries.RemoveAt(0);
        }
    }

    public bool TryPop(out T snapshot)
    {
        if (_entries.Count == 0)
        {
            snapshot = default!;
            return false;
        }

        var last = _entries[^1];
        _entries.RemoveAt(_entries.Count - 1);
        _totalBytes -= last.SizeBytes;
        snapshot = last.Snapshot;
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _totalBytes = 0;
    }
}
