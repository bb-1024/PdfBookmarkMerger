using PdfBookmarkMerger.App.Options;

namespace PdfBookmarkMerger.App.Services;

/// <summary>
/// ユーザー設定の読み取り(Options経由)と、ユーザー設定ファイルへの書き戻しを行う。
/// </summary>
public interface IUserSettingsService
{
    PdfBookmarkMergerOptions Current { get; }

    Task SaveAsync(PdfBookmarkMergerOptions options, CancellationToken ct = default);
}
