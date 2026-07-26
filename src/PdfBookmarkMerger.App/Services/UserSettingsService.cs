using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfBookmarkMerger.App.Options;

namespace PdfBookmarkMerger.App.Services;

public sealed class UserSettingsService(
    IOptionsMonitor<PdfBookmarkMergerOptions> optionsMonitor,
    ILogger<UserSettingsService> logger) : IUserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PdfBookmarkMergerOptions Current => optionsMonitor.CurrentValue;

    public async Task SaveAsync(PdfBookmarkMergerOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);

        var document = new Dictionary<string, PdfBookmarkMergerOptions>
        {
            [PdfBookmarkMergerOptions.SectionName] = options,
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);

        // 書き込み中のプロセス強制終了等でファイルが壊れてJSONとして読めなくなるのを避けるため、
        // 同一フォルダの一時ファイルに書いてから置き換える(同一ボリューム内のFile.Moveはアトミック)。
        var tempPath = AppPaths.UserSettingsFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, AppPaths.UserSettingsFilePath, overwrite: true);

        logger.LogInformation("ユーザー設定を保存しました: {Path}", AppPaths.UserSettingsFilePath);
    }
}
