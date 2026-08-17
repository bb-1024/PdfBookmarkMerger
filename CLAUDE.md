# CLAUDE.md

このファイルは、このリポジトリでClaude Codeが作業する際のプロジェクト固有の文脈・規約をまとめたものです。

## プロジェクト概要

**PdfBookmarkMerger** — 複数のPDFファイルをドラッグ&ドロップで結合し、各ファイルのしおり(Outline)を
自動抽出・編集した上で1つのPDFに書き出すデスクトップアプリ。Windows向け **WPF-UI版** と
macOS向け **Avalonia版** の2フロントエンドを、共通のドメイン層・ViewModel層の上に構築している。

- ランタイム: .NET 10
- MVVM基盤: [Reactive.Bindings](https://github.com/runceel/ReactiveProperty)(`ReactivePropertySlim<T>` / `ReactiveCommand` / `AsyncReactiveCommand`)
- PDF処理: [PDFsharp](https://github.com/empira/PDFsharp) 6.2.4
- テスト: xUnit + [Shouldly](https://github.com/shouldly/shouldly)
- バージョンの単一の情報源: リポジトリ直下の `Directory.Build.props`(`<Version>`/`<AssemblyVersion>`/`<FileVersion>`)。全プロジェクトがこれを共有する。

設計ドキュメントは `docs/`(日本語版`docs/ja/`・英語版`docs/en/`、それぞれMarkdown+スタイル付きHTML)に
あり、`docs/index.html` が入口。**このリポジトリで何かを変更する前に、関連する章
(特に `01-architecture` と、触る層に対応する `02`〜`04`)に目を通すこと。** バージョン間の設計差分は
`05-version-history` に、実際のコミットハッシュ付きでまとめてある。

## アーキテクチャ上の必須ルール

- 依存方向は一方向: `Wpf`/`Avalonia` → `App` → `Core`。`Core`・`App` はUIフレームワークに一切依存しない
  (`IDialogService` などのインターフェースを介して各フロントエンドに実装を委ねる)。
- **UIに関わる変更は必ずWPF版・Avalonia版の両方に実装する。** 一方だけ直して他方を忘れる事故が
  起きやすい(`PdfBookmarkMerger.UiConverters.Tests` の `ConverterParityTests` はこれを検知するための
  ゴールデンテスト)。ただし両実装の出力が常に完全一致すべきとは限らない
  (例: `DepthToTitleWidthConverter` のインデント幅はWPF=19px/Avalonia=16pxで意図的に異なる。
  「両実装が食い違わないこと」ではなく「各実装が自身の正しい定数通りに計算していること」を検証する)。
- **大量ノードを一括で触るしおりツリー操作は、必ずチャンク処理する。** `BookmarkTreeViewModel` には
  200件超のノードを対象とする操作(結合前ページ数の再計算、ツリー開閉レベルの一括指定など)を
  `RecomputeChunkSize`(200)件ごとに `await Task.Yield()` を挟みながら処理し、`IsBusy`/`BusyProgress`
  経由で既存の処理中オーバーレイへ進捗を転送する確立されたパターンがある(v1.2.1で大量しおり時の
  フリーズ修正として導入、v1.2.2のツリー開閉レベル機能でもそのまま再利用)。新しく「全ノードを走査して
  UIバインディングを書き換える」操作を追加する場合は、このパターンを流用すること。
- `ReactiveCommand`のCanExecuteは、上記のチャンク処理中(`IsBusy`)を`!IsBusy`として必ず含める
  (処理中オーバーレイによるブロックだけに頼らない多重防御。`UndoCommand`/`CollapseAllCommand`/
  `ExpandAllCommand` が実例)。
- i18nの文言追加は **3ファイル同時に** 更新する: `Strings.resx`(既定=日本語)・`Strings.en.resx`
  (英語)・`Strings.cs`(手書きのアクセサ。ResXFileCodeGeneratorによる自動生成ではなく、
  `dotnet build`単体でも確実に動くよう手書きしている)。`StringsTests.cs` の
  `EveryStringProperty_HasBothJapaneseAndEnglishTranslations` が対応漏れを検知する。

## ビルド・テスト

```
dotnet build PdfBookmarkMerger.slnx
dotnet test PdfBookmarkMerger.slnx -c Debug --nologo
dotnet test PdfBookmarkMerger.slnx -c Release --nologo
```

- リリース前・大きめの変更後は **Debug/Release両方** でテストを実行して確認する。
- テストは3プロジェクト(`Core.Tests`/`App.Tests`/`UiConverters.Tests`)。大量ノードを扱う機能を
  追加した場合は、既存の `BookmarkTreeLargePageNumberRecomputeTests.cs`/`BookmarkTreeExpandLevelTests.cs`
  のように、小規模ツリー(IsBusyが一度もtrueにならない)・大規模ツリー(チャンク処理される)の
  両方を検証するテストを追加する。

## 実行環境に関する注意(重要)

- **`pwsh` はインストールされていない。** Windows PowerShell 5.1のみ。`.ps1` スクリプトは
  `pwsh ./scripts/xxx.ps1` ではなく、PowerShellツールから直接 `./scripts/xxx.ps1` を実行すること。
- BashツールとPowerShell/Read/Edit/Globツールとで、カレントディレクトリの扱いが食い違うことがある
  (Bashツールの`cd`がツール呼び出しをまたいでリセットされる場合がある)。相対パスで失敗した場合は
  `pwd`/`ls`で現在地を確認し、必要ならフルパス(`\\mac\Home\Downloads\Windows\claude\sandbox1\PdfBookmarkMerger`)
  を使う。複数コマンドを同じ作業ディレクトリで実行したい場合は `cd ... && ...` のように1回の呼び出しで
  連結する。

## リリース作業(バージョンタグ)

- Gitタグは **annotated tag**(`git tag -a vX.Y.Z <commit> -m "vX.Y.Z"`)を使う既存の慣習がある。
  `05-version-history` のタグ一覧表に載せている「コミット」欄は、タグが指すコミット自体のハッシュ
  **ではなく**、annotated tagオブジェクト自身のハッシュ(`git rev-parse vX.Y.Z`で取得)である点に注意
  (`git cat-file -t vX.Y.Z` が `tag` を返すことで確認できる)。
- `dist/` と `ref/` は完全に`.gitignore`対象。リリースノート(`.md`)・publishしたzip・フォルダは
  **絶対にコミットしない**。
- 手順の詳細は `release` スキルを参照(`/release`、または「vX.Y.Zとしてリリースして」等の依頼で
  自動的に案内される)。
- **`git push` は明示的に指示された場合のみ実行する。** ローカルコミットまでは指示なしで進めてよいが、
  push・タグのpushは都度ユーザーの許可を得る。

## 開発の進め方(このリポジトリでの既存の慣習)

- 個々の不具合修正・機能追加は「修正 → コードレビュー → テスト → 設計ドキュメント更新 → コミット」の
  単位で進める(1件ごとにコミットを分ける)。詳細は `dev-cycle` スキルを参照。
- 設計ドキュメント(`docs/`)は実装と同時に更新する。特に:
  - 各章の見出しに付く `<span class="badge ok">vX.Y.Z〜</span>` のようなバージョンバッジは、
    「その変更が実際にどのバージョンで出荷されたか」を示す。**過去のバッジは既存のバージョンのまま
    残し、新しいバージョンでの変更には新しいバッジを追加する。**
  - バージョン間差分(`05-version-history`)に書く内容は、記憶やコミットメッセージの推測ではなく
    `git log --oneline <前タグ>..<対象タグ>` / `git show <commit>` で実際に確認してから書く。
  - テスト件数・バージョン番号などの数値は、ドキュメント全体(`docs/ja`・`docs/en`、`.md`と`.html`
    双方、`README.md`)で一貫させる。定期的に(`docs-audit` スキル等で)ズレがないか点検する。
- 応答は原則として日本語で行う(このプロジェクトでのユーザーとのやり取りは一貫して日本語)。
