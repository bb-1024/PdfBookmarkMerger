---
name: docs-audit
description: PdfBookmarkMergerの設計ドキュメント(docs/ja・docs/en)と実際のリポジトリ状態(テスト件数・バージョン・git履歴・READMEのリンク等)にズレが無いか網羅的に点検し、見つかった差分を修正する。「設計ドキュメントの見直しを行います」「ドキュメントとの差分が無いか点検してください」のような依頼で使う。
---

# 設計ドキュメントの整合性点検

過去に実施した点検で、テスト件数の記載違い(合計は合っているのに内訳が古いまま)、
バージョンバッジの誤り(実際にはv1.2.1で出荷された変更がv1.2.0のバッジのままになっていた)、
フォルダ移動後に残った古いパス参照、といった具体的な不整合が見つかっている。
**記憶や「たぶんこうだったはず」で判断せず、必ず実コマンドの出力で裏を取ること。**

## 1. テスト件数

各テストプロジェクトの実際の件数を取得する:

```
dotnet test tests/PdfBookmarkMerger.Core.Tests/PdfBookmarkMerger.Core.Tests.csproj --list-tests --nologo
dotnet test tests/PdfBookmarkMerger.App.Tests/PdfBookmarkMerger.App.Tests.csproj --list-tests --nologo
dotnet test tests/PdfBookmarkMerger.UiConverters.Tests/PdfBookmarkMerger.UiConverters.Tests.csproj --list-tests --nologo
```

`docs/ja/00-overview.{md,html}`・`docs/en/00-overview.{md,html}`(プロジェクト構成表内)・
`README.md`(英語/日本語セクション両方)の件数・合計と突き合わせる。

## 2. バージョン番号・日付

- `Directory.Build.props` の `<Version>` と、各ドキュメントのヘッダー
  (`最終更新`/`Last updated`、`Design Document — vX.Y.Z` のようなeyebrow、バージョンチップ)を
  突き合わせる。
- `git tag -l --sort=v:refname` と `05-version-history` のタグ一覧表を突き合わせる。

## 3. バージョンバッジの正確性

各章にある `<span class="badge ok">vX.Y.Z〜</span>`(または対応するMarkdownの太字表記)について、
**そのバッジが指すバージョンで実際にその変更が入ったか**を裏取りする。特に注意が必要なケース:
あるバージョンで機能Aを追加し、直後の別バージョンで機能Aに関連する修正・拡張を行った場合、
修正・拡張側のバッジが誤って機能Aと同じバージョンのままになっていないか。

```
git log --oneline <前タグ>..<対象タグ> -- src tests   # 区間ごとの実際のコミット一覧
git show <commit>                                        # 個別コミットの内容確認
```

バッジの記述と実際のコミットが指すバージョンが食い違っていたら、バッジ側を修正する
(バッジは「実際に出荷されたバージョン」を示すものであり、後から書いたドキュメントの都合で
動かしてはならない)。

## 4. リンク・アンカーの健全性

`docs/`配下のHTMLファイル間の相互参照(`href="...html#anchor"`)が、実在するファイル・
`id="..."`に解決するか確認する:

```bash
for f in docs/index.html docs/ja/*.html docs/en/*.html; do
  dir=$(dirname "$f")
  grep -oE 'href="[^"]+"' "$f" | sed -E 's/href="([^"]+)"/\1/' | while read -r href; do
    case "$href" in http*|mailto:*) continue ;; esac
    path="${href%%#*}"; anchor="${href#*#}"
    [ "$path" = "$href" ] && anchor=""
    target=$([ -z "$path" ] && echo "$f" || realpath -m "$dir/$path" 2>/dev/null)
    [ -n "$path" ] && [ ! -f "$target" ] && echo "$f -> BROKEN FILE: $href"
    if [ -n "$anchor" ] && [ "$anchor" != "$href" ]; then
      grep -q "id=\"$anchor\"" "$target" 2>/dev/null || echo "$f -> BROKEN ANCHOR: $href"
    fi
  done
done
```

`README.md`・各種コメント(`scripts/*.ps1`、テストファイルのdocコメント等)にある
`docs/design.html`のような**旧パス**への参照も、リポジトリ全体で検索して洗い出す:

```
grep -rn "docs/design\.html\|docs/detailed-design\.html\|docs/manual-verification-checklist" \
  README.md scripts/ tests/ src/ docs/
```
(`docs/`配下のヒットは章間の意図した相互参照である可能性があるため個別に判断し、
`docs/`配下以外でのヒットは基本的に修正対象。)

## 5. HTMLタグの対応

```bash
for f in docs/index.html docs/ja/*.html docs/en/*.html; do
  for tag in div section h2 h3 h4 p ul li table thead tbody tr td pre code a figure; do
    o=$(grep -o "<$tag[ >]" "$f" | wc -l); c=$(grep -o "</$tag>" "$f" | wc -l)
    [ "$o" != "$c" ] && echo "$f: $tag open=$o close=$c"
  done
done
```

## 6. 修正・報告

見つかった差分は原則としてその場で修正する。修正内容をユーザーに簡潔に報告する
(何を、なぜ、どう直したか)。`.md`と`.html`の両方に同じ内容の記載がある場合は、
**両方**を同期させる(片方だけ直して終わらせない)。
