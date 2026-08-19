# 02. Core層設計

`PdfBookmarkMerger.Core` はUIフレームワークに依存しないドメイン層で、PDFの読み書きと
しおり結合ロジックを持つ。すべての公開サービスは `Core/ServiceCollectionExtensions.AddPdfBookmarkMergerCore()`
でSingleton登録される。

## 1. モデル(`Core/Models`)

| 型 | 役割 |
|---|---|
| `BookmarkNode` | しおり1件。`Title`・`DestinationType`(表示方法)・座標(`Left`/`Top`/`Right`/`Bottom`/`Zoom`)・`IsOpen`・`Children` を持つ。`SourceFileEntryId`+`OriginalPageIndex` の組が、抽出元PDF内でのジャンプ先ページを一意に特定する不変の識別情報。`MergedPageIndex` は結合後PDFにおけるページ番号(表示用の副次情報)。`PageOffset` はしおり設定画面での結合前ページ数の直接編集による差分(未編集時null、実際のPDF結合には一切影響しない書き出し・表示専用の調整値)。`Clone()` で自身+子孫の深いコピー(Idは再採番)を返す。 |
| `PdfFileEntry` | 結合対象ファイル一覧の1エントリ。`FilePath`・`PageCount`(未確定時null)。 |
| `PdfFileMetadata` | 1ファイルから読み取った `PageCount`・`Bookmarks`・`Properties` の集合。 |
| `PdfDocumentPropertiesModel` | Title/Author/Subject/Keywords/Creator。結合後ファイルの既定プロパティは、結合対象の先頭PDFの値を流用する。 |
| `PdfMergeRequest` | `PdfMergeService.MergeAsync` への入力(ファイル順序・編集済みしおりツリー・出力プロパティ・保存先)。 |
| `MergeProgress` | `record(int CompletedFileCount, int TotalFileCount, string CurrentFileName)`。結合処理の進捗通知。 |
| `BookmarkDestinationType` | `XYZ` / `Fit` / `FitH` / `FitV` の4種のみ(バウンディングボックス指定の `FitB*`・矩形指定の `FitR` はUI非対応、読込時に簡略化する)。 |
| `PdfRect` | `record struct(double Left, double Bottom, double Right, double Top)`。PDFユーザー空間(pt、左下原点)の矩形。**フィールド順はPDF仕様の `/Rect` 配列順であり、見た目の読み順(Left,Top,Right,Bottom)とは異なる**。位置引数での構築は取り違えの罠になるため、必ず名前付き引数で構築する(実際に取り違えのバグが発生し、テストで検出・修正した実例が [§2.10](#link-editor-services) にある)。 |
| `LinkAnnotationNode` | リンク編集画面で作成・読み取った1件のリンク注釈。`GroupId`(複数行選択から生成された一連のリンクをUI上でまとめて扱うための集約キー。単一行選択なら`GroupId==Id`)・`SourcePageIndex`/`SourceRect`(ホットスポット)・`TargetPageIndex`+`DestinationType`+座標(ジャンプ先、`BookmarkNode`と同じ形の表現)を持つ。`BookmarkNode`と共通の基底型には括り出していない(3行程度の重複より早すぎる抽象化を避ける、という方針)。 |
| `PdfTextLetter` | `record struct(string Value, PdfRect Rect)`。文字(グリフ)1つとそのPDFユーザー空間上の矩形。文字単位の範囲選択のヒットテストに使う。 |

`BookmarkNode` は、しおりツリー上の位置(`SourceFileEntryId`+`OriginalPageIndex`)と
「結合後どこに表示されるか」(`MergedPageIndex`)を意図的に分離している。これにより、
結合順の並べ替えやファイル追加・削除があっても、ジャンプ先ページの特定に使う識別情報自体は
変化しない。

## 2. ドメインサービス(`Core/Services`)

### 2.1 `PdfFileCollectorService`

`ExpandToPdfFilePaths(droppedPaths)` — D&D/ダイアログで渡されたパス(ファイル・フォルダ混在可)を
実際のPDFファイルパス一覧へ展開する。フォルダは直下のみを対象(子フォルダは非対象)とし、
拡張子 `.pdf` 以外・存在しないパスは無視してログに記録する。

### 2.2 `PdfMetadataService`

- `ReadPageCountAsync(filePath)` — ページ数のみを高速に読み取る(ファイル一覧表示直後の
  暫定ページ数表示用)。
- `ReadMetadataAsync(file)` — ページ数・しおりツリー・ドキュメントプロパティをまとめて読み取る
  (しおり抽出段階で使用)。

しおり抽出(`ExtractOutlines`)は `PdfOutlineCollection` を再帰的に辿り、各アウトラインの
ジャンプ先ページを、ページオブジェクトの参照比較で構築した辞書(`BuildPageIndexLookup`、
`ReferenceEqualityComparer` 使用)から解決する。ジャンプ先ページが特定できないしおりは
警告ログを出して読み飛ばす。

2つの既知のPDFsharp 6.2.4回避策をこのサービス内に閉じ込めている。

1. **NaN座標の正規化**(`AsFiniteOrNull`) — 宛先タイプによって `Left`/`Top`/`Right`/`Bottom`/`Zoom`
   がNaN/Infinityを返すことがある。そのまま保持するとUndoスナップショットのJSON化で例外になり、
   出力PDFへも不正な値のまま書き戻されるため、未指定を表す `null` へ正規化する。
2. **開閉状態の読み取り**(`ReadOpened`) — `PdfOutline.Opened` は開閉状態を表す `/Count` の符号を
   正しく解釈できない既知の不具合があるため、`/Count` を直接読み取って回避する
   (`/Count` 自体は正しく書き込まれている)。`/Count` が存在しない葉ノード等はライブラリの
   既定値にフォールバックする。

### 2.3 `MissingBookmarkFallback`

`ResolveEffectiveBookmarks(orderedFiles, metadataByFileId)` — しおりを1件も持たないPDFについて、
ファイル名(拡張子なし)をタイトルとするしおりを補った「実効しおりリスト」をファイルごとに
解決する。表示方法(`DestinationType`)は直前のファイルの設定を参考にする(座標は引き継がない)。
入力は変更せず、都度新規生成した結果を返す非破壊設計。

### 2.4 `BookmarkOffsetCalculator`

`ComputeMergedBookmarks(orderedFiles, effectiveBookmarksByFileId, metadataByFileId)` —
ファイル結合順に基づき累積ページ数オフセットを計算し、各しおりの `MergedPageIndex` を設定した
複製ツリー(ファイル順に連結)を返す。入力は変更しない。

### 2.5 `PdfMergeService`

`MergeAsync(request, progress, ct)` は2フェーズで構成される。

1. **フェーズ1(並列)**: 各入力PDFを `PdfReader.Open` で開く(ディスクI/O・構造解析)。
   `SemaphoreSlim` で同時実行数を `Math.Clamp(Environment.ProcessorCount, 1, 8)` に制限し、
   スレッドプール枯渇・ファイルハンドル過多を避ける。
2. **フェーズ2(単一スレッド)**: 開いた各PDFのページを出力 `PdfDocument` へ追加する
   (`AddPage`、高速なメモリ内コピーが中心)。ページ追加のたびに `MergeProgress` を報告する。

その後 `ApplyBookmarks` が `(SourceFileEntryId, OriginalPageIndex)` → 実ページのマップを使って
出力側にアウトラインを再構築する。子を持つノードでは、PDFsharp 6.2.4が第1階層以外で `/Count`
(開閉状態)を書き込まない既知の不具合を、保存前に `Elements.SetInteger("/Count", ...)` を
直接呼んで回避する。

フェーズ1で一部のファイルだけ開けた状態(パスワード保護・破損・他プロセスによるロック、または
キャンセル)でも、既に開けた分は必ず `Dispose` するよう、フェーズ1・2全体を1つの `try/finally` で
囲んでいる。

<a id="link-remap"></a>
**ページ内リンクのジャンプ先付け替え(v1.2.3〜)**: PDFsharpの `AddPage` はページ内リンク注釈
(`/Subtype /Link`)自体は複製するが、しおりと異なり、リンクのジャンプ先(`/Dest` または
`/A`(GoToアクション)の `/D`)が参照するページオブジェクトまでは結合後のものに書き換えない。
そのため結合前は放置すると、リンク先が結合後のPDF内に存在しないオブジェクトを指す
(ダングリング参照になる)、または無関係な別ページを指してしまう不具合があった。
`ApplyBookmarks` と同じ `pageMap` の考え方を用い、ファイルごとに構築した
「ソースページオブジェクトのID → 元ページ番号」の対応表(`sourcePageIndexByObjectId`)経由で
ジャンプ先の元ページ番号を特定し、`pageMap` で結合後の実ページに解決してから、コピー済み注釈の
`/Dest`・`/A/D` 配列の先頭要素(ページ参照)を直接書き換える。名前付きジャンプ先
(`/Dest` が名前・文字列で表現されるもの)は対象外。

### 2.6 `BookmarkSettingsExportService`(v1.2.0〜)

`ExportAsync(bookmarks, outputPath, ct)` — しおりツリーを「しおり設定ファイル仕様」
(UTF-8のXML、ルート `<Bookmark>` 直下に `<Title Page="..." Action="GoTo">` を入れ子で並べる形式)へ
書き出す。`Page` 属性はPDF Referenceの表示方法(Fit/FitH/FitV/XYZ)と同じ引数順で出力し、
未設定(null)の引数は仕様上0と同義であるため0を代用する。XML宣言行は `XmlWriter` の既定出力
(小文字 `"utf-8"`)が仕様書の例と一致しないため、手書きで出力している。

### 2.7 `BookmarkDestinationTypeMapper`(internal)

`Core.Models.BookmarkDestinationType` ⇔ PDFsharpの `PdfPageDestinationType` を相互変換する。
Modelsをライブラリ非依存に保つため、変換ロジックはServices層に配置している。

<a id="link-editor-services"></a>
### 2.8 `PdfPageRenderer`(v1.3.0〜)

リンク編集画面のプレビュー用に、PDFの1ページをPNGバイト列へ描画する。`PDFtoImage`
(PDFium — Chromeのネイティブ(非JS)PDF表示と同じ描画エンジン — のラッパー)を使う。

- `RenderPageAsync(filePath, pageIndex, scale, ct)` — `Conversion.ToImage(...)` を呼びPNGを返す。
- `GetPageSizeAsync(filePath, pageIndex, ct)` — ページのPDFユーザー空間サイズ(pt)を返す。

`PDFtoImage` の公開APIは完全にステートレス(ファイルパス/バイト列を毎回渡すワンショットAPIのみで、
文書ハンドルを保持して使い回す公開APIは存在しない)。設計時点では「開いた文書ハンドルを保持して
使い回す」想定だったが、実装着手時に判明したため、2000ページ級のサンプルPDFで簡易ベンチマークを
取った(自前のPDFsharp製生成ツールで作成)。結果は**ページ位置に関わらず1ページあたり16〜26ms**で
安定しており、ステートレスであることによる速度上の問題はないと判断した。PDFium自体はスレッド
セーフでないため、`SemaphoreSlim`(上限1)で呼び出しを直列化する。PDFium呼び出し箇所は
[01-architecture.md §4.6](01-architecture.md) の通り、クラス単位ではなく該当2〜3行だけを
`#pragma warning disable/restore CA1416` で囲んでいる。

### 2.9 `PdfTextExtractor`(v1.3.0〜)

`ExtractLettersAsync(filePath, pageIndex, ct)` — `UglyToad.PdfPig`(純粋管理コード、ネイティブ
依存なし)で指定ページの文字(グリフ)ごとの矩形+文字を抽出し `PdfTextLetter` の一覧を返す。
`PdfPig`の`Letter.GlyphRectangle`はPDFユーザー空間(pt、左下原点)で、PDFsharpの`/Rect`と
同じ座標系のため、`PdfPig`↔`PdfSharp`間の追加変換は不要(PDF空間↔描画ビットマップのピクセル空間
の変換のみ`PdfCoordinateMapper`が担う)。文書全体は一括抽出せず、現在表示中のページのみを
対象とする。

### 2.10 `PdfCoordinateMapper`(static、v1.3.0〜)

PDFユーザー空間(pt、左下原点)と、`PdfPageRenderer`が描画したビットマップのピクセル座標
(左上原点、`96 * scale / 72` px/pt)を相互変換する。

```csharp
public static double PixelsPerPoint(float scale) => 96.0 * scale / 72.0;
public static (double X, double Y) ToPixel(double pdfX, double pdfY, double pageHeightPt, float scale);
public static (double X, double Y) ToPdf(double pixelX, double pixelY, double pageHeightPt, float scale);
public static PdfRect ToPixelRect(PdfRect pdfRect, double pageHeightPt, float scale);
```

`ToPixelRect`は実装時に一度、`PdfRect`を位置引数で誤った順序(Left,Top,Right,Bottom)で構築し
Top/Bottomが入れ替わるバグが実際に発生した(専用テストで検出)。`PdfRect`のフィールド順が
PDF仕様順(Left,Bottom,Right,Top)であり見た目の読み順と異なることが原因のため、§1
の通り常に名前付き引数で構築する。

### 2.11 `PdfLinkAnnotationService`(v1.3.0〜)

リンク編集画面で作成したリンクの書き込み・PDFに元から含まれるリンクの読み取りを担う。

- `ApplyLinksAsync(filePath, links, ct)` — `PdfDocumentOpenMode.Modify`
  (`AddPage`によるページ再構築を伴わない、PDFsharpの追記専用オープンモード)で開き、既存の
  `PdfPage`オブジェクトへ直接`/Annots`エントリを追加して同じパスへ保存する。**`AddPage`を
  一切使わない**のが重要な設計判断で、`AddPage`経由でページを再構築すると、§2.5で修正した
  「内部リンクのジャンプ先が結合後に壊れる」不具合と同種の問題を、既存のしおり・既存リンクに
  対して再度引き起こしうるため。リンク注釈自体も
  高水準の`PdfLinkAnnotation.CreateDocumentLink`は使わず(ページ番号ベースの単純な`/XYZ`しか
  組み立てられないため)、`PdfDictionary`/`PdfArray`を直接構築する低レベルAPIで
  `BookmarkDestinationType`の全パターンを再現する。`/Border`を明示的に`[0 0 0]`(幅0)で
  書き込む点にも注意(省略時のPDF仕様上の既定値`[0 0 1]`だと、ビューワによってはリンク範囲に
  矩形の枠線が表示されてしまう)。
- `ReadExistingLinksAsync(filePath, ct)` — 逆に、ファイルに既に含まれる`/Subtype /Link`の注釈を
  読み取り`LinkAnnotationNode`の一覧として返す。`/A`(`/S /GoTo /D [...]`)経由・`/Dest`直接指定の
  いずれの形式にも対応する。ジャンプ先配列の先頭要素(ページへの間接参照)は、`PdfReference.ObjectID`
  をキーにした辞書(全ページを1度だけ走査して構築)で実ページ番号へ解決する。書き込み→読み取りの
  ラウンドトリップをテストで検証している。App層側での扱い(既存リンクは編集・削除不可)は
  [03-app-design.md §7.6](03-app-design.md#link-editor-existing-links) を参照。

## 3. Core層の処理パイプライン全体図

<img src="../diagrams/merge-pipeline.svg" alt="Core層の処理パイプライン" width="100%" />

図中、破線より上の4段階(`PdfFileCollectorService` → `PdfMetadataService` →
`MissingBookmarkFallback` → `BookmarkOffsetCalculator`)はいずれも入力を変更しない
読み取り専用の変換(`Clone()` ベース)であり、`BookmarkTreeViewModel.Load` に渡った時点で
初めて編集可能な状態(App層)になる。編集後のツリーは `ToModel()`(PDF結合用)・
`ToExportModel()`(しおり設定ファイル書き出し用、結合前ページ数編集の連鎖反映込み)の
2通りの形で再びCore層のサービスへ渡される。
