using Xunit;

// Strings.Cultureはプロセス全体で共有される静的状態(PdfBookmarkMerger.App.Resources.Strings)であり、
// 一部のテスト(StringsTests, AppLanguageBootstrapperTests)がこれを書き換える。xUnitは既定でテスト
// クラス間を並列実行するため、並列化したままだと他のテスト(既定=日本語のハードコード文字列を
// 前提とするテスト)がまれに失敗する(表示言語が英語のまま漏れ出すため)。テスト数も少なく
// 並列化の恩恵が小さいため、アセンブリ全体で無効化して確実性を優先する。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
