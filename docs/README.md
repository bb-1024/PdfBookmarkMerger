# docs — PdfBookmarkMerger 設計ドキュメント / Design Documentation

**日本語** | [English](#english)

このディレクトリはPdfBookmarkMergerの設計ドキュメントです。リポジトリのタグ付きバージョン
(`v1.0.0` 〜 現行版)の実ソースコード・コミット履歴を出発点として書き起こしています。
各章はMarkdown(`.md`)とスタイル付きHTML(`.html`)の両方で提供しています。ブラウザで読む場合は
[`index.html`](index.html)(日英切替の索引ページ)から、GitHub上でMarkdownとして読む場合は
下記の`ja/`・`en/`フォルダから直接どうぞ。

- [`ja/`](ja/00-overview.md) — 日本語版(00〜05の6ファイル)
- [`en/`](en/00-overview.md) — 英語版(同構成)
- [`diagrams/`](diagrams/) — 両言語から共有するSVG図(3点)
- [`assets/`](assets/) — HTML版が参照する共通スタイルシート

読み始めは各言語フォルダの `00-overview` から。バージョン間の設計差分とその確認コマンドは
`05-version-history` にまとめています。

---

<a id="english"></a>
## English

This directory holds PdfBookmarkMerger's design documentation, grounded in the actual source code
and commit history of the repository's tagged versions (`v1.0.0` through the current release). Each
chapter is available both as Markdown (`.md`) and as styled HTML (`.html`). Browse
[`index.html`](index.html) (a bilingual landing page) in a browser, or read the Markdown directly
from the `ja/`/`en/` folders on GitHub.

- [`ja/`](ja/00-overview.md) — Japanese edition (6 files, 00–05)
- [`en/`](en/00-overview.md) — English edition (same structure)
- [`diagrams/`](diagrams/) — 3 SVG diagrams shared by both editions
- [`assets/`](assets/) — Shared stylesheet used by the HTML edition

Start with `00-overview` in either language folder. Cross-version design diffs and the exact
commands to verify them yourself are in `05-version-history`.
