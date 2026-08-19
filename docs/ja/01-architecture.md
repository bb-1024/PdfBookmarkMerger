# 01. アーキテクチャ

## 1. レイヤー構成

<img src="../diagrams/architecture.svg" alt="レイヤー・プロジェクト依存関係図" width="100%" />

- **`PdfBookmarkMerger.Core`**: PDFの読み書き・しおり抽出・結合ロジックを持つドメイン層。
  `PdfSharp` にのみ依存し、WPF/AvaloniaいずれのUI関連型も参照しない。
- **`PdfBookmarkMerger.App`**: ViewModel群とアプリ共通サービス(設定保存、i18n、Undo、
  Generic Hostの組み立て)を持つ層。UIフレームワーク固有の型は一切参照せず、代わりに
  `IDialogService` などのインターフェースを介して各フロントエンドに実装を委ねる
  (依存性逆転)。
- **`PdfBookmarkMerger.Wpf` / `PdfBookmarkMerger.Avalonia`**: それぞれのUIフレームワーク上で
  `App` 層のViewModelをバインドし、D&D・ダイアログ表示・Converterなどフレームワーク固有の
  処理を実装する。

依存の向きは常に「UIフレームワーク → App → Core」の一方向であり、Core・Appはテストしやすく
(実ウィンドウなしで動作確認できる)、UIフレームワークの差し替え・追加も比較的局所化される。

## 2. DIとホスト組み立て

`PdfBookmarkMergerHostFactory.Build(args, configureUiServices)`(`src/PdfBookmarkMerger.App/PdfBookmarkMergerHostFactory.cs`)
がWPF版・Avalonia版共通のホスト組み立て処理を担う。

1. `AppPaths.AppDataDirectory` / `AppPaths.LogDirectory` を作成する
   (`%AppData%/PdfBookmarkMerger` 配下。レジストリは使わない)。
2. `appsettings.json`(実行ファイル同梱)→ ユーザー設定ファイル(`settings.json`)の順に
   `IConfiguration` へ読み込む(後段が優先)。
3. Serilogを構成する(日次ローリングファイル、14日保持、Debug構成のみコンソール追加)。
4. `PdfBookmarkMergerOptions` をConfigurationの `"PdfBookmarkMerger"` セクションへバインドする。
5. `Core.ServiceCollectionExtensions.AddPdfBookmarkMergerCore()` と
   `App.ServiceCollectionExtensions.AddPdfBookmarkMergerApp()` でCore・App層のサービス/ViewModelを
   登録する(いずれもSingleton)。
6. 呼び出し元(`Wpf.App` / `AvaloniaApp.App`)が渡す `configureUiServices` コールバックで、
   `IDialogService` の実装(`WpfDialogService` / `AvaloniaDialogService`)と `MainWindow` を登録する。

登録されるサービス/ViewModel(抜粋):

| 層 | 登録内容 |
|---|---|
| Core | `IPdfFileCollectorService`, `IPdfMetadataService`, `IPdfMergeService`, `IBookmarkSettingsExportService`, `IPdfPageRenderer`, `IPdfTextExtractor`, `IPdfLinkAnnotationService` |
| App | `IUserSettingsService`, `FileListViewModel`, `BookmarkTreeViewModel`, `LinkEditorViewModel`, `MainWindowViewModel` |
| UIフレームワーク側 | `IDialogService`, `MainWindow` |

`IPdfPageRenderer`/`IPdfTextExtractor`/`IPdfLinkAnnotationService` はリンク編集画面
(`WorkflowStep.EditLinks`)向けのCore層サービス。詳細は
[02-core-design.md §2.8〜§2.11](02-core-design.md#link-editor-services) を参照。

<a id="startup"></a>
## 3. 起動シーケンス

WPF版(`Wpf.App.OnStartup`)・Avalonia版(`AvaloniaApp.App.OnFrameworkInitializationCompleted`)は
ほぼ同一の手順を踏む。

1. `PdfBookmarkMergerHostFactory.Build(...)` でホストを構築・`Start()`。
2. `AppLanguageBootstrapper.ApplyAsync(userSettings)` を **`MainWindow` を構築する前に** 同期的に
   待ち合わせて実行し、表示言語(`Strings.Culture`)を確定させる
   (XAMLの `x:Static` 参照はウィンドウ構築・XAML読込時点の値で固定されるため、後から
   切り替えることができない)。
3. `MainWindow` をDIコンテナから取得し、`ThemeApplier.Apply(...)` でテーマ(ライト/ダーク/システム)を
   適用してから表示する。

未処理例外は、`Wpf.App` コンストラクタ / Avalonia版 `Program.Main` それぞれで可能な限り早期に
グローバルフックへ登録し、`PdfBookmarkMergerHostFactory.LogUnhandledException(...)` 経由でSerilogへ
必ず記録する(無言でクラッシュしてログに何も残らない状態を避けるため)。AvaloniaにはWPFの
`DispatcherUnhandledException` に相当するUIスレッド専用フックが存在しないため、代わりに
`StartWithClassicDesktopLifetime(args)` 呼び出し全体を`try/catch`で囲んでいる。

## 4. 横断的関心事

### 4.1 i18n(日本語/英語)

- `App/Resources/Strings.resx`(既定=日本語)・`Strings.en.resx`(英語)から自動生成される
  `Strings` クラスの `Culture` プロパティで、参照する文言セットを切り替える。
- `AppLanguageBootstrapper.ApplyAsync` が起動時に一度だけ、設定済み言語(`PdfBookmarkMergerOptions.Language`)
  があればそれを、無ければOSのUI言語から自動判定して確定・保存する。
- 設定ダイアログでの言語変更は、`AppLanguageBootstrapper.ApplyImmediate` で `Strings.Culture` を
  即座に切り替えた上で、**同じViewModelインスタンスを引き継いだ新しい `MainWindow` を再構築して
  差し替える**(`WpfDialogService`/`AvaloniaDialogService` の `ReplaceMainWindowForLanguageChange`)。
  `x:Static` 参照はウィンドウ構築時点で固定されるため、既存ウィンドウの文言をその場で
  書き換えることはできない。

### 4.2 Undo

`App/Undo/UndoHistory<T>`(ジェネリックなスナップショットスタック)を `BookmarkTreeViewModel` が
`string`(ツリー全体のJSON)特化で使用する。詳細は [03-app-design.md](03-app-design.md#undo) を参照。

### 4.3 busy / progress の表示

`MainWindowViewModel.IsBusy` / `BusyProgress`(`ReactivePropertySlim<BusyProgressInfo?>`)を
唯一の「処理中」状態として扱い、以下のいずれからも同じUI(処理中オーバーレイ、5秒経過後にのみ
表示する詳細進捗テキスト)を再利用する。

- ファイル読込(`ConfirmFilesAsync`)
- PDF結合(`MergeAsync`)
- **`BookmarkTreeViewModel.IsBusy`/`BusyProgress` の転送**(v1.2.1〜。大量しおりの編集・Undo時の
  チャンク処理。詳細は [03-app-design.md](03-app-design.md#recompute) と
  [05-version-history.md](05-version-history.md#v121) を参照)

### 4.4 設定・ログの保存先

`AppPaths`(`src/PdfBookmarkMerger.App/AppPaths.cs`)が `%AppData%/PdfBookmarkMerger/` 配下に
設定ファイル(`settings.json`)・ログ(`logs/`)をまとめる。レジストリは使用せず、実行ファイルの
配置場所(読み取り専用の可能性がある)にも依存しない。設定ファイルの書き込みは、同一フォルダの
一時ファイルへ書いてから `File.Move(..., overwrite: true)` で原子的に置き換える方式
(`UserSettingsService.SaveAsync`)で、書き込み中のプロセス強制終了によるJSON破損を避けている。

### 4.5 App層の非同期メソッドは `ConfigureAwait(false)` を使わない(v1.3.0〜)

ViewModelの非同期メソッド(`LoadAsync`・`RecomputeAllPageNumberDisplaysAsync` 等)は、
`await` に `ConfigureAwait(false)` を付けない。WPF版はコマンドの `CanExecuteChanged` を
`CommandManager`(UIスレッド専用)経由で処理するため、`ConfigureAwait(false)` を付けると
最初の `await` 以降の継続処理がスレッドプールスレッドで実行され、その中で
`ReactivePropertySlim<T>.Value` を書き換えた瞬間に `InvalidOperationException`
(クロススレッドアクセス)が発生する。この既定を意図的に破った箇所が
[03-app-design.md §7.7](03-app-design.md#link-editor-thread) に事例として残っている。

### 4.6 PDFium呼び出し箇所への `[SupportedOSPlatform]` の付け方(v1.3.0〜)

`IPdfPageRenderer` の実装はPDFium(ネイティブライブラリ)を呼ぶため .NETアナライザーが
CA1416(プラットフォーム互換性)を警告する。クラス単位・アセンブリ単位で
`[SupportedOSPlatform]` を付けると、そのアセンブリ内の無関係な型(`BookmarkNode` 等)まで
プラットフォーム制限が波及し大量の警告を誘発するため、実際にPDFiumを呼ぶ2〜3行だけを
`#pragma warning disable/restore CA1416` で囲む(理由をコメントで残す)。詳細は
[02-core-design.md §2.8](02-core-design.md#link-editor-services) を参照。
