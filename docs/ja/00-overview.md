# PdfBookmarkMerger 設計ドキュメント — 00. 概要

> **このドキュメントについて**
> 本ドキュメント群は、リポジトリに存在するタグ付きバージョン
> (`v1.0.0` / `v1.1.0` / `v1.2.0` / `v1.2.1` / `v1.2.2`)の
> **実際のソースコード・コミット履歴** を出発点として書き起こしています。
> 各記述がどのバージョンの何のコミットに由来するかは [05-version-history.md](05-version-history.md)
> で個別に追跡できるようにしています。英語版は `docs/en/` に同じ構成で置いています。

## 1. これは何のアプリか

**PdfBookmarkMerger** は、複数のPDFファイルを1つに結合しつつ、結合後PDFのしおり(Outline)を
自動抽出・手動編集できるデスクトップアプリです。Windows向けの **WPF(WPF-UI)版** と、
Windows/macOS向けの **Avalonia版** の2フロントエンドを、共通のドメイン層・ViewModel層の上に
構築しています。

主な機能:

- 結合対象PDFファイルの指定(ドラッグ&ドロップ、ファイル/フォルダ選択ダイアログ)と並べ替え
- 各PDFからのしおり自動抽出、しおりを持たないPDFへのファイル名しおりの自動補完
- しおりツリーの編集(タイトル・表示方法・座標・開閉状態・階層・並び順)、Undo対応
- 結合前ページ数の直接編集(該当ページ以降・後続ファイルへの連鎖反映)
- PDF結合・保存、しおり設定ファイル(XML)単体でのエクスポート
- 日本語/英語UI切り替え、ライト/ダーク/システム追従テーマ

## 2. 技術スタック

| 分類 | 内容 |
|---|---|
| ランタイム | .NET 10 |
| WPF版UI | WPF-UI (`Wpf.Ui.Controls.FluentWindow`) |
| Avalonia版UI | Avalonia (Fluentテーマ、`WithInterFont()`) |
| MVVM基盤 | [Reactive.Bindings](https://github.com/runceel/ReactiveProperty) (`ReactivePropertySlim<T>`, `ReactiveCommand`, `AsyncReactiveCommand`) |
| DI / ホスティング | `Microsoft.Extensions.Hosting` の Generic Host + `Microsoft.Extensions.DependencyInjection` |
| 設定 | `Microsoft.Extensions.Configuration`(`appsettings.json` + ユーザー設定ファイルの2階層) |
| ログ | Serilog(日次ローリングファイル、Debug構成のみコンソール追加) |
| PDF処理 | [PDFsharp](https://github.com/empira/PDFsharp) 6.2.4 |
| テスト | xUnit + [Shouldly](https://github.com/shouldly/shouldly) |

## 3. プロジェクト構成(v1.2.2時点)

```
src/
  PdfBookmarkMerger.Core/       ドメイン層(モデル・PDF入出力・結合ロジック、UI非依存)
  PdfBookmarkMerger.App/        ViewModel層・アプリ共通サービス(UI非依存、DI組み立ての中心)
  PdfBookmarkMerger.Wpf/        WPF-UIフロントエンド(net10.0-windows)
  PdfBookmarkMerger.Avalonia/   Avaloniaフロントエンド(net10.0)
tests/
  PdfBookmarkMerger.Core.Tests/         Core層のテスト(24件)
  PdfBookmarkMerger.App.Tests/          App層ViewModelのテスト(91件)
  PdfBookmarkMerger.UiConverters.Tests/ WPF/Avalonia双方のConverterを実行するゴールデンテスト(33件)
  sample/                                手動確認・回帰テスト用の実サンプルPDF
tools/
  PdfBookmarkMerger.SampleGenerator/    tests/sample配下のサンプルPDFを生成する補助ツール
scripts/
  publish-wpf-release.ps1               WPF版のリリースビルド(自己完結・単一ファイル)生成スクリプト
```

依存方向は一方向(`Wpf`/`Avalonia` → `App` → `Core`)で、`Core`・`App` はいずれもUIフレームワークに
依存しません。詳細は [01-architecture.md](01-architecture.md) を参照してください。

## 4. アプリの基本フロー(4ステップ)

`MainWindowViewModel.Step`(`WorkflowStep`列挙型)が画面遷移を管理します。

1. **ファイル指定**(`WorkflowStep.SelectFiles`) — D&D/ダイアログでPDFを追加し、並び順を確定する。
   この並び順がそのまま結合順になる。
2. **しおり抽出**(`WorkflowStep.EditBookmarks` に入る直前) — `ConfirmFilesCommand` が全ファイルの
   メタデータを並列読み込みし、しおり抽出・結合後ページ番号の計算までを行う。
3. **しおり編集**(`WorkflowStep.EditBookmarks`) — 抽出結果をツリーとして編集する。
4. **結合・保存** — `MergeCommand` でPDFを結合・保存する(または `SaveBookmarkSettingsCommand` で
   しおり設定ファイルのみを書き出す)。

## 5. ドキュメント構成

| ファイル | 内容 |
|---|---|
| [01-architecture.md](01-architecture.md) | レイヤー構成・DI組み立て・横断的関心事(ログ・i18n・Undo・busy/progress) |
| [02-core-design.md](02-core-design.md) | Core層: モデル・ドメインサービスの詳細設計 |
| [03-app-design.md](03-app-design.md) | App層: ViewModel・アプリサービスの詳細設計 |
| [04-ui-design.md](04-ui-design.md) | WPF/Avaloniaフロントエンドの詳細設計(コードビハインド・Converter) |
| [05-version-history.md](05-version-history.md) | v1.0.0→v1.1.0→v1.2.0→v1.2.1→v1.2.2の設計上の差分と、その確認方法 |

## 6. この文書の裏付け方法

本ドキュメント群の内容は、以下の方法で実際のリポジトリ状態から確認しています。

- 現行設計の記述: HEAD(`v1.2.2`)時点の該当ソースファイルを直接参照
- バージョン間差分の記述: `git diff --name-status <前バージョンタグ> <対象バージョンタグ> -- src tests`
  で変更ファイル一覧を洗い出した上で、各差分の内容を個別に確認
- テスト件数: `dotnet test <プロジェクト>.csproj --list-tests` の実出力件数(HEAD時点)

読者自身が再確認する場合は、[05-version-history.md](05-version-history.md) 末尾のコマンド例を
そのまま実行してください。
