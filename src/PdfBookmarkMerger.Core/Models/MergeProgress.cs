namespace PdfBookmarkMerger.Core.Models;

/// <summary>
/// PDF結合処理(<see cref="Services.IPdfMergeService.MergeAsync"/>)の進捗通知。
/// </summary>
public sealed record MergeProgress(int CompletedFileCount, int TotalFileCount, string CurrentFileName);
