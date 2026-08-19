using PdfBookmarkMerger.App.Options;
using PdfBookmarkMerger.App.Services;
using PdfBookmarkMerger.Core.Models;
using PdfBookmarkMerger.Core.Services;

namespace PdfBookmarkMerger.App.Tests.TestHelpers;

/// <summary>渡されたパスをそのままPDFファイルとして扱う(フォルダ展開は行わない)フェイク。</summary>
internal sealed class FakeFileCollectorService : IPdfFileCollectorService
{
    public IReadOnlyList<string> ExpandToPdfFilePaths(IEnumerable<string> droppedPaths) => droppedPaths.ToList();
}

/// <summary>
/// ファイルパスに応じて、事前に登録したメタデータを返す、または例外を投げるフェイク。
/// 実PDFファイルを用意せずに「読み込みに失敗するファイル」を再現するために使う。
/// </summary>
internal sealed class FakeMetadataService : IPdfMetadataService
{
    private readonly Dictionary<string, PdfFileMetadata> _metadataByPath = [];
    private readonly HashSet<string> _failingPaths = [];

    public void RegisterSuccess(string filePath, int pageCount, IReadOnlyList<BookmarkNode>? bookmarks = null)
    {
        _metadataByPath[filePath] = new PdfFileMetadata
        {
            FileEntryId = Guid.Empty,
            PageCount = pageCount,
            Bookmarks = bookmarks?.ToList() ?? [],
            Properties = PdfDocumentPropertiesModel.CreateEmpty(),
        };
    }

    public void RegisterFailure(string filePath) => _failingPaths.Add(filePath);

    public Task<int> ReadPageCountAsync(string filePath, CancellationToken ct = default) =>
        Task.FromResult(_metadataByPath.TryGetValue(filePath, out var metadata) ? metadata.PageCount : 0);

    public Task<PdfFileMetadata> ReadMetadataAsync(PdfFileEntry file, CancellationToken ct = default)
    {
        if (_failingPaths.Contains(file.FilePath))
        {
            throw new InvalidOperationException($"テスト用の意図的な失敗: {file.FilePath}");
        }

        if (!_metadataByPath.TryGetValue(file.FilePath, out var metadata))
        {
            throw new InvalidOperationException($"テストで未登録のファイル: {file.FilePath}");
        }

        // FileEntryIdは呼び出し元のfile.Idに合わせて実測時と同様に差し替える。
        return Task.FromResult(new PdfFileMetadata
        {
            FileEntryId = file.Id,
            PageCount = metadata.PageCount,
            Bookmarks = metadata.Bookmarks,
            Properties = metadata.Properties,
        });
    }
}

/// <summary>実際には描画せず、固定の1バイトPNGもどきを返すだけのフェイク。</summary>
internal sealed class FakePdfPageRenderer : IPdfPageRenderer
{
    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, float scale, CancellationToken ct = default) =>
        Task.FromResult<byte[]>([0x89, 0x50, 0x4E, 0x47]);

    public Task<(double Width, double Height)> GetPageSizeAsync(string filePath, int pageIndex, CancellationToken ct = default) =>
        Task.FromResult((595.0, 842.0));
}

/// <summary>実際にはPDFを結合せず、最後に受け取ったリクエストを記録するだけのフェイク。</summary>
internal sealed class FakeMergeService : IPdfMergeService
{
    public PdfMergeRequest? LastRequest { get; private set; }

    public int CallCount { get; private set; }

    public Task MergeAsync(PdfMergeRequest request, IProgress<MergeProgress>? progress = null, CancellationToken ct = default)
    {
        LastRequest = request;
        CallCount++;
        progress?.Report(new MergeProgress(request.Files.Count, request.Files.Count, request.Files.LastOrDefault()?.FileName ?? string.Empty));
        return Task.CompletedTask;
    }
}

/// <summary>実際にはXMLを書き出さず、最後に受け取った引数を記録するだけのフェイク。</summary>
internal sealed class FakeBookmarkSettingsExportService : IBookmarkSettingsExportService
{
    public IReadOnlyList<BookmarkNode>? LastBookmarks { get; private set; }

    public string? LastOutputPath { get; private set; }

    public int CallCount { get; private set; }

    public Task ExportAsync(IReadOnlyList<BookmarkNode> bookmarks, string outputPath, CancellationToken ct = default)
    {
        LastBookmarks = bookmarks;
        LastOutputPath = outputPath;
        CallCount++;
        return Task.CompletedTask;
    }
}

/// <summary>ダイアログ操作を全て固定値で即応答するフェイク。</summary>
internal sealed class FakeDialogService : IDialogService
{
    public string? SaveDialogResult { get; set; } = @"C:\out\merged.pdf";

    public string? SaveBookmarkSettingsDialogResult { get; set; } = @"C:\out\merged.xml";

    public PdfDocumentPropertiesModel? PropertiesDialogResult { get; set; }

    public int? LevelCapDialogResult { get; set; }

    public (int MinLevel, int MaxLevel)? LastLevelCapDialogRange { get; private set; }

    public List<(string Title, string Message)> Errors { get; } = [];

    public List<(string Title, string Message)> Infos { get; } = [];

    public Task<IReadOnlyList<string>> ShowOpenPdfFilesDialogAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> ShowOpenFolderDialogAsync() => Task.FromResult<string?>(null);

    public Task<string?> ShowSaveMergedPdfDialogAsync(string suggestedFileName, string? initialDirectory) =>
        Task.FromResult(SaveDialogResult);

    public Task<string?> ShowSaveBookmarkSettingsDialogAsync(string suggestedFileName, string? initialDirectory) =>
        Task.FromResult(SaveBookmarkSettingsDialogResult);

    public Task<PdfDocumentPropertiesModel?> ShowPropertiesDialogAsync(PdfDocumentPropertiesModel initial) =>
        Task.FromResult(PropertiesDialogResult);

    public Task<PdfBookmarkMergerOptions?> ShowSettingsDialogAsync(PdfBookmarkMergerOptions current) =>
        Task.FromResult<PdfBookmarkMergerOptions?>(null);

    public Task<int?> ShowLevelCapDialogAsync(int minLevel, int maxLevel)
    {
        LastLevelCapDialogRange = (minLevel, maxLevel);
        return Task.FromResult(LevelCapDialogResult);
    }

    public void ShowError(string title, string message) => Errors.Add((title, message));

    public void ShowInfo(string title, string message) => Infos.Add((title, message));
}

/// <summary>メモリ上に保持するだけのフェイク設定サービス。</summary>
internal sealed class FakeUserSettingsService : IUserSettingsService
{
    public PdfBookmarkMergerOptions Current { get; private set; } = new();

    public Task SaveAsync(PdfBookmarkMergerOptions options, CancellationToken ct = default)
    {
        Current = options;
        return Task.CompletedTask;
    }
}
