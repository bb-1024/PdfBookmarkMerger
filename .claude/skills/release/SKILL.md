---
name: release
description: PdfBookmarkMergerを新しいバージョンとしてリリースする(バージョン更新・ビルド・テスト・publish・設計ドキュメント同期・リリースノート作成・コミット・タグ・push)。「vX.Y.Zとしてリリースして」「リリース作業をして」のような依頼で使う。
---

# PdfBookmarkMergerのリリース手順

このスキルは、`v1.2.1`・`v1.2.2` のリリースで実際に踏んだ手順を一般化したものです。
各ステップは実際にこの手順で検証済みです。省略・順序の入れ替えをしないこと。

## 0. 前提確認

- 対象バージョン番号(X.Y.Z)をユーザーに確認する(明示されていれば不要)。
- `git push`・タグのpushまで実行してよいか、ユーザーの指示を確認する
  (明示的に「push」「タグ」の実行を指示されていない場合は、コミットまでで止める)。
- `git status --short` で作業ツリーの状態を確認し、リリース対象外の変更が紛れ込んでいないか確認する。

## 1. バージョン番号を更新する

`Directory.Build.props`(リポジトリ直下、唯一のバージョン情報源)の3箇所を更新する。

```xml
<Version>X.Y.Z</Version>
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
```

## 2. テストを実行する(Debug・Release両方)

```
dotnet test PdfBookmarkMerger.slnx -c Debug --nologo
dotnet test PdfBookmarkMerger.slnx -c Release --nologo
```

3プロジェクト(Core.Tests / App.Tests / UiConverters.Tests)すべてが両構成で成功することを確認する。
失敗がある場合はリリース作業を中断し、原因を修正してから再度このステップからやり直す。

## 3. リリースビルドを生成する

PowerShellツールから直接スクリプトを実行する(`pwsh`はこの環境に存在しないため、
`pwsh ./scripts/...` ではなく `./scripts/...` で直接呼ぶこと)。

```
./scripts/publish-wpf-release.ps1
```

`Directory.Build.props` の `<Version>` を自動的に読み取り、
`dist/PdfBookmarkMerger-Wpf-vX.Y.Z-win-x64/`(展開済みフォルダ)と同名の`.zip`を生成する。
`dist/` は`.gitignore`対象なので、生成物をコミット対象に含めないよう注意する
(`git add`する前に`git status`で確認する)。

## 4. 設計ドキュメントをバージョンに同期する

対象: `docs/ja/*.{md,html}`・`docs/en/*.{md,html}`(各6章)。今回のリリースに含まれる各変更について:

1. 該当する層のドキュメント(`02-core-design`/`03-app-design`/`04-ui-design`)に、変更点を
   `<span class="badge ok">vX.Y.Z〜</span>`(HTML版)/`**vX.Y.Z〜**`(Markdown版)付きで追記する。
   相互参照用のアンカー(`<a id="..."></a>` / `id="..."`)を付け、必要なら他の章から
   `[03-app-design.md §2.7](03-app-design.md#anchor-name)` の形でリンクする。
2. `05-version-history` の「タグ一覧」表に新しい行を追加する(日付は実際のコミット日、
   コミット欄は下記5番のタグ作成後に `git rev-parse vX.Y.Z` で取得したハッシュを入れる —
   このリポジトリのタグはannotated tagのため、タグ自身のオブジェクトハッシュであり、
   コミット自体のハッシュとは異なる)。
3. `05-version-history` に新しいバージョンの節を追加する。**各変更の説明には、実際のコミットハッシュを
   引用すること。** 記憶や推測で書かず、`git log --oneline <前タグ>..HEAD -- src tests` /
   `git show <commit>` で内容を確認してから書く。
4. タイトルの `(v1.0.0 → v1.2.1)` のようなバージョン範囲、ヘッダーの日付・バージョンチップ、
   直前のバージョン節にあった「執筆時点の最新バージョン」的な文言(次のバージョンが来たら古くなる)を
   更新・削除する。
5. テスト件数やプロジェクト構成が変わった場合は `00-overview`(プロジェクト構成表・テスト件数)と
   `README.md`(テスト件数の記載)も更新する。
6. 全HTMLファイルでタグの対応が取れているか確認する(次のコマンドで開始/終了タグ数を比較):
   ```
   for f in docs/index.html docs/ja/*.html docs/en/*.html; do
     for tag in div section h2 h3 p ul table a; do
       o=$(grep -o "<$tag[ >]" "$f" | wc -l); c=$(grep -o "</$tag>" "$f" | wc -l)
       [ "$o" != "$c" ] && echo "$f: $tag open=$o close=$c"
     done
   done
   ```

## 5. コミットする

このリポジトリの慣習に合わせ、性質の異なる変更は分けてコミットする(例:
「機能追加+バージョン番号更新」を1コミット、「ドキュメント整理」を別コミット)。
コミットメッセージは英語・命令形・「なぜ」を説明する本文、というこのリポジトリの既存コミットの
スタイルに合わせる(`git log --oneline -10` で確認できる)。

## 6. タグを作成する

```
git tag -a vX.Y.Z <対象コミットのハッシュ> -m "vX.Y.Z"
git rev-parse vX.Y.Z   # このハッシュを4-2.のタグ一覧表に反映する(必要ならdocsコミットを追加する)
```

タグは通常「機能追加・バージョン番号更新」のコミット(ドキュメント整理コミットより前)を指す
(以前のリリースでも、タグ付け後のドキュメント同期コミットはタグを付け直していない)。

## 7. リリースノートを作成する

`dist/PdfBookmarkMerger-vX.Y.Z-release-notes.md` を日英併記(既存リリースノートと同じ構成:
`**[English](#english) | [日本語](#日本語)**` → `## English` セクション → `## 日本語` セクション)で作成する。
既存の `dist/PdfBookmarkMerger-v1.2.1-release-notes.md` を書式の参考にする(`dist/`はgitignore対象
なので、過去のリリースノートは`git log`ではなくファイルシステム上に残っている場合のみ参照できる)。
このファイルは**コミットしない**。

## 8. push(明示的に指示された場合のみ)

```
git push origin main
git push origin vX.Y.Z
```

`git push --tags` のようにタグを一括pushするのではなく、対象タグ名を明示して個別にpushする
(意図しないタグの混入を避けるため)。
