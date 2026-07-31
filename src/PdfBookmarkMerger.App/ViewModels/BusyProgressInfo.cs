namespace PdfBookmarkMerger.App.ViewModels;

/// <summary>
/// 時間のかかる処理(ファイル読み込み・PDF結合)の進捗状況。
/// UI側は、処理開始から一定時間(5秒)経過後にのみこの内容を表示する運用を想定する。
/// </summary>
public sealed record BusyProgressInfo(int CompletedCount, int TotalCount, IReadOnlyList<string> CurrentFileNames);
