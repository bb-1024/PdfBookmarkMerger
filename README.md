# PdfBookmarkMerger

**[English](#english) | [日本語](#日本語)**

---

## English

A desktop application that merges multiple PDF files via drag & drop, automatically extracting each file's bookmarks (outlines) with correct page offsets, letting you edit the combined bookmark tree, and writing the result out as a single PDF.

Built on a shared ReactiveProperty MVVM core with two native UI front ends: **WPF-UI** on Windows and **Avalonia UI** on macOS.

### Features

- Add PDF files or whole folders via drag & drop or file dialogs; reorder and remove them before merging.
- Automatic bookmark extraction with cumulative page-offset calculation; files without bookmarks are given a fallback bookmark from their file name.
- Full bookmark-tree editor: rename, change destination type (Fit/XYZ/...), edit jump coordinates, add/remove nodes, promote/demote levels, drag & drop to reparent or reorder (with auto-scroll near the tree edges), and cap the depth of a subtree.
- Undo for tree edits, with history automatically capped by memory usage rather than a fixed step count.
- Export the current bookmark tree as a standalone bookmark-settings XML file, independent of running an actual PDF merge.
- Editable pre-merge page numbers: changing a bookmark's pre-merge page number shifts every bookmark at or after that page (by the source PDF's own page structure, not tree order) in the same file, cascading the post-merge page numbers of that file and every file merged after it. The text box auto-sizes to the number of digits and highlights when it carries an active edit; right-click offers "reset" for the whole file, clearing every edit applied anywhere in that file, not just the clicked row. Merging is disabled whenever such an edit is active, and disabled together with the export whenever the edit would produce a page number below 1.
- Parallelized, progress-reporting loads and merges so large bookmark-heavy batches stay responsive.
- Japanese and English UI, auto-detected on first launch from the OS language and changeable from Settings.
- No registry use: `settings.json` and log files are both stored under the per-user `%AppData%/PdfBookmarkMerger/` folder.

### Requirements

- Windows 10 or later (WPF-UI build) — self-contained, no separate .NET runtime install needed.
- macOS (Avalonia build) — cross-compiled and publish-verified from this repository; see [docs/design.html](docs/design.html) for the current build/verification status.

### Building from source

```
dotnet build PdfBookmarkMerger.slnx
dotnet test PdfBookmarkMerger.slnx
```

To produce a release build for Windows (self-contained, single-file, compressed):

```
pwsh ./scripts/publish-wpf-release.ps1
```

This writes both the unpacked folder and a `.zip` archive to `dist/` (git-ignored). See [docs/design.html §11](docs/design.html#windows-release-build) for details, including the native DLLs that must ship alongside the executable.

### Project layout

```
src/PdfBookmarkMerger.Core/      Domain models + PDF read/merge logic (PDFsharp), UI-independent
src/PdfBookmarkMerger.App/       Shared ReactiveProperty ViewModel layer, Generic Host bootstrap, i18n
src/PdfBookmarkMerger.Wpf/       Windows front end (WPF-UI)
src/PdfBookmarkMerger.Avalonia/  macOS front end (Avalonia UI)
tests/                           xUnit + Shouldly test suites (Core / App / UI converters)
tools/PdfBookmarkMerger.SampleGenerator/  Developer CLI that regenerates the sample PDFs under tests/sample/
docs/                            Design documentation (see below)
scripts/                         Release build scripts
```

### Documentation

- [Design Document](docs/design.html) — architecture, tech stack, feature-by-requirement mapping, build/release procedures.
- [Detailed Design Document](docs/detailed-design.html) — class diagrams and a full member/method reference.
- [Manual Verification Checklist](docs/manual-verification-checklist.html) — QA steps for UI behavior not covered by automated tests.

(Each document also has a `.en.html` English counterpart linked from its own table of contents.)

### Testing

```
dotnet test PdfBookmarkMerger.slnx
```

128 tests across three projects: `PdfBookmarkMerger.Core.Tests`, `PdfBookmarkMerger.App.Tests`, and `PdfBookmarkMerger.UiConverters.Tests` (which exercises both frontends' converter classes directly).

---

## 日本語

複数のPDFファイルをドラッグ&ドロップで結合し、各ファイルのしおり(Outline)をページオフセット付きで自動抽出・編集したうえで、1つのPDFに書き出すデスクトップアプリケーションです。

ReactiveProperty MVVMを核とする共通アプリケーション層を、Windows版(**WPF-UI**)とmacOS版(**Avalonia UI**)の2つのネイティブUIで共有しています。

### 主な機能

- ドラッグ&ドロップまたはダイアログで、PDFファイル・フォルダを追加。結合前に並べ替え・削除が可能。
- しおりの自動抽出と累積ページオフセット計算。しおりを持たないファイルには、ファイル名から自動生成したしおりを補完。
- しおりツリーの編集: タイトル変更、表示方法(Fit/XYZ等)の変更、ジャンプ先座標の編集、ノードの追加・削除、レベルの上げ下げ、ドラッグ&ドロップによる並べ替え・再親子付け(ツリー端付近での自動スクロール対応)、子孫の階層深さの上限設定。
- しおりツリー編集の「元に戻す」に対応。履歴は固定回数ではなく、使用メモリ量に応じて自動的に管理。
- 現在のしおりツリーを、PDF結合を実行せずに単独のしおり設定XMLファイルとして書き出し可能。
- 結合前ページ数を編集可能。変更すると、同一ファイル内でそのページ(元となるPDFのページ構造上の位置基準、しおりツリー上の順序ではない)以降の全しおりの結合前ページ数、およびそのファイル・後続ファイルの結合後ページ数が一律で連動する。テキストボックスは桁数に応じて幅が自動調整され、差分が加わっている行は強調表示。右クリックの「リセット」は、クリックした行だけでなくそのファイル全体の編集を一括で取り消す。編集中は「結合してPDFを保存」を非活性化し、結果ページ数が1未満になる場合は「しおり設定ファイルを保存」も非活性化する。
- 読み込み・結合処理の並列化と進捗表示により、大量のしおりを含むファイルでも画面が固まらない。
- 日本語・英語のUIに対応。初回起動時はOSの言語から自動判定し、以後は設定画面で変更可能。
- 設定はレジストリを使わず、`settings.json`・ログともにユーザーごとの`%AppData%/PdfBookmarkMerger/`フォルダに保存。

### 動作環境

- Windows 10以降(WPF-UI版) — 自己完結型ビルドのため、別途.NETランタイムのインストールは不要。
- macOS(Avalonia版) — 本リポジトリ上でクロスコンパイル・パブリッシュの成功までを確認済み。現時点の検証状況は[docs/design.html](docs/design.html)を参照。

### ソースからのビルド

```
dotnet build PdfBookmarkMerger.slnx
dotnet test PdfBookmarkMerger.slnx
```

Windows向けのリリースビルド(自己完結型・単一ファイル・圧縮)を作成する場合:

```
pwsh ./scripts/publish-wpf-release.ps1
```

展開済みフォルダと`.zip`アーカイブの両方が`dist/`(git管理外)に出力されます。実行ファイルに同梱が必要なネイティブDLL等の詳細は[docs/design.html 第11節](docs/design.html#windows-release-build)を参照してください。

### プロジェクト構成

```
src/PdfBookmarkMerger.Core/      ドメインモデル・PDF入出力/結合ロジック(PDFsharp)、UI非依存
src/PdfBookmarkMerger.App/       共有ReactiveProperty ViewModel層、Generic Hostブートストラップ、i18n
src/PdfBookmarkMerger.Wpf/       Windows版フロントエンド(WPF-UI)
src/PdfBookmarkMerger.Avalonia/  macOS版フロントエンド(Avalonia UI)
tests/                           xUnit + Shouldlyによるテスト一式(Core / App / UI Converters)
tools/PdfBookmarkMerger.SampleGenerator/  tests/sample/配下のサンプルPDFを再生成する開発者向けCLI
docs/                            設計ドキュメント一式(下記参照)
scripts/                         リリースビルド用スクリプト
```

### ドキュメント

- [設計ドキュメント](docs/design.html) — アーキテクチャ・技術選定・要件対応表・ビルド/公開手順。
- [詳細設計ドキュメント](docs/detailed-design.html) — クラス図・各クラスの関数リファレンス。
- [UIコードビハインド 手動確認手順書](docs/manual-verification-checklist.html) — 自動テスト化していないUI挙動の目視確認チェックリスト。

(各ドキュメントの目次から、英語版`.en.html`にもリンクしています。)

### テスト

```
dotnet test PdfBookmarkMerger.slnx
```

`PdfBookmarkMerger.Core.Tests` / `PdfBookmarkMerger.App.Tests` / `PdfBookmarkMerger.UiConverters.Tests`(両フロントエンドのConverterを実際に実行するゴールデンテスト)の3プロジェクトで、計128件のテストを実施しています。
