# 05. バージョン間の設計差分(v1.0.0 → v1.3.1)

このドキュメントは、`git diff --name-status <前バージョン> <対象バージョン> -- src tests` で
洗い出した変更ファイル一覧と、該当コミットの内容(`git show <commit>`)を個別に確認した上で
まとめています。末尾の「確認方法」の節にあるコマンドで、誰でも同じ手順を再現できます。

## タグ一覧

| タグ | 日付 | コミット |
|---|---|---|
| `v1.0.0` | 2026-07-27 | `12611ca` |
| `v1.1.0` | 2026-08-01 | `4fc46b1` |
| `v1.2.0` | 2026-08-03 | `9822c18` |
| `v1.2.1` | 2026-08-12 | `b61d2ee` |
| `v1.2.2` | 2026-08-17 | `0bc7147` |
| `v1.2.3` | 2026-08-18 | `2bd2b45` |
| `v1.3.0` | 2026-08-20 | `3056af1` |
| `v1.3.1` | 2026-08-20 | `db31720` |

---

## v1.0.0(2026-07-27) — 初版

最初のタグ付きリリース。[00-overview.md](00-overview.md) 〜 [04-ui-design.md](04-ui-design.md) に
記載した設計のうち、以下は **この時点でまだ存在しない**(後続バージョンで追加された)点に注意。

- 日英切り替え(i18n) — `AppLanguageBootstrapper` / `Strings.resx` 系は v1.1.0 で追加
- Undo — `UndoHistory<T>` は v1.1.0 で追加
- busyオーバーレイ・進捗表示 — `BusyProgressInfo` / `IsBusy` は v1.1.0 で追加
- しおり設定ファイル(XML)出力 — `BookmarkSettingsExportService` は v1.2.0 で追加
- 結合前ページ数の編集 — `BookmarkNode.PageOffset` は v1.2.0 で追加

v1.0.0時点で既に存在した主な機能: PDFファイル/フォルダのD&D追加、しおり自動抽出
(`PdfMetadataService`)としおり無しPDFへのファイル名補完(`MissingBookmarkFallback`)、
しおりツリーの基本編集(タイトル・表示方法・座標・追加/削除/D&D並べ替え・レベル上限切り詰め)、
PDF結合(`PdfMergeService`)とプロパティ編集ダイアログ、設定・ログの保存(Serilog日次ローリング)、
グローバル未処理例外のログ記録。

---

## v1.1.0(2026-08-01)

v1.0.0から28個のコミットが積まれた、機能追加・不具合修正ともに最も多い区間。
`git diff --name-status v1.0.0 v1.1.0 -- src tests` で56ファイルが変更されている。

### 追加された機能

- **日英i18n**(`822a2d8`) — `Strings.resx`/`Strings.en.resx`、`AppLanguageBootstrapper` による
  初回起動時の自動言語判定・保存。設定ダイアログでの即時切替は後発の `7e49524` で対応
  (テーマモードと同じ扱いへ統一)。
- **Undo**(`b9ad0c5`) — RAM使用量ベースで上限管理する `UndoHistory<T>` をしおりツリー編集に追加。
- **一括読込・結合の高速化 + busyオーバーレイ**(`6db6849`) — `ConfirmFilesAsync`(メタデータ並列読込)・
  `PdfMergeService`(PDFオープン並列化)を上限付き並列実行化し、処理中は全コントロールを不活性化する
  busyオーバーレイ+5秒経過後の詳細進捗(完了/総数・処理中ファイル名)を導入。
  **この「busyオーバーレイ+`BusyProgressInfo`」という枠組みが、v1.2.1で`BookmarkTreeViewModel`の
  チャンク処理からも再利用されることになる([v1.2.1の節](#v121)参照)。**
- レベル上げ/下げボタン(`57d531a`)、レベル上限ダイアログに自身のレベルを選択肢へ含める(`1daaf72`)、
  空白余白クリックでの行選択(`23f3d38`)、D&D中のツリー端での自動スクロール(`0b42ad5`)。
- ファイル一覧: 複数選択ブロックのまとめ移動(`87c85ac`)、境界での上下移動ボタン非活性化(`65b6817`)。
- ダイアログをEscapeでキャンセル可能に(`f8ac748`)、初期ウィンドウの拡大・ボタン再配置(`1bf3602`)。

### 修正された不具合(3件の異なる「フリーズ」系不具合)

これらは症状としてはいずれも「操作するとアプリが固まる」ように見えるが、**原因はそれぞれ異なる**。

1. **初回起動時にウィンドウが一切表示されないデッドロック**(`28a4dce`) —
   `UserSettingsService.SaveAsync` が `File.WriteAllTextAsync` を `ConfigureAwait(false)` 無しで
   awaitしていた。初回起動時は `AppLanguageBootstrapper.ApplyAsync(...).GetAwaiter().GetResult()`
   経由でUIスレッドを同期的にブロックした状態からこのメソッドが呼ばれるため、継続処理が
   (ブロックされて塞がっている)UIスレッドへの復帰を試みてデッドロックし、`OnStartup` が
   永久に返らずウィンドウが一切表示されないまま固まっていた。`settings.json.tmp`
   (原子的置き換えの一時ファイル)がリネームされずに残っていたことが、停止位置を直接示す
   証拠だった。修正は該当箇所へ `ConfigureAwait(false)` を追加するのみ。
2. **NaN座標によるしおりツリー編集のフリーズ**(`87a2de7`) — PDFsharpの `PdfOutline.Left`/`Top`/
   `Right`/`Bottom`/`Zoom` は、宛先タイプによって該当項目が無い場合NaNを返す。これをそのまま
   `BookmarkNode` へ保持していたため、構造編集(`Move`/`PromoteLevel`/`DemoteLevel`/
   `SetChildLevelCapAsync`)のたびに呼ばれる `PushUndoSnapshotCore` のJSON化
   (`System.Text.Json`は非有限のdoubleで例外を投げる)で未処理例外になり、実際に抽出した
   PDFに対する最初の編集操作がフリーズしたように見えていた。`PdfMetadataService` に
   `AsFiniteOrNull` を追加し、非有限値を「未指定」を表すnullへ正規化して解決した
   (Core層・App層の両方に回帰テストを追加。App層側のテストが現在の
   `BookmarkTreeEditFreezeReproTests` — 実サンプルPDFを本番同様のCoreサービスで読み込み、
   各編集操作が10秒以内に完了することを検証する)。
3. **大量しおり時のUIフリーズ** — これは本区間(v1.1.0)ではなく、[v1.2.1](#v121)で修正された、
   ノード数200件超のツリーでの同期処理起因の別の不具合。

### 設定ファイル保存先の紆余曲折

`1ac63b8`(実行ファイルと同じフォルダへ変更・ポータブル化を試みる)→ `87a6bb4`
(`%AppData%/PdfBookmarkMerger/` へ、ログと同じ場所に統一)という順で変更されている。
実行ファイルの配置場所が読み取り専用の可能性を考慮し、最終的に書き込みが保証される
AppDataフォルダへ統一した。

### その他の修正

- `9805b89`: 一度試みた「しおり行の背景をストレッチしてドラッグのヒットテストを全幅にする」
  案(`7892ff1`)を、クリック時に意図しない横スクロールが発生する副作用のため差し戻し。
  この教訓は後の [v1.2.1の横スクロール修正](#v121) に引き継がれている。
- `2ad5394`: Avalonia版のファイル/しおりD&Dの `PointerPressed` 購読をTunnelフェーズへ変更
  (`SelectingItemsControl` の既定選択処理がBubbleフェーズで先取りしてしまう問題への対応)。
- `70f50dd`: 並列PDFオープンの一部が失敗した場合、既に開けたファイルを確実にDispose。
- `41286b1`: `SystemThemeWatcher` の二重登録防止、ウィンドウClose時のUnwatch漏れ修正。
- `5de9ab9`: `Right`/`Bottom` 座標も `Left`/`Top`/`Zoom` と同じくUndo追跡対象に。
- `e486342`: Avalonia版の保存ダイアログ既定フォルダをWPF版と同じ「ドキュメント」フォルダへ。
- `5daeabd`: Serilogのコンソール出力をDebug構成限定に。
- `1077825`: Avaloniaのメインループ全体を`try/catch`で囲み、UIスレッド外まで伝播した例外も
  確実にログへ残す(WPFの`DispatcherUnhandledException`に相当するフックがAvaloniaに無いため)。

---

## v1.2.0(2026-08-03)

4個のコミット。しおり設定ファイル出力と、結合前ページ数編集機能をまとめて追加。

- **`de060a2` しおり設定ファイル(XML)エクスポート** — `IBookmarkSettingsExportService`/
  `BookmarkSettingsExportService` を新設。PDF結合を実行せずに、しおり構成だけを
  「しおり設定ファイル仕様」のXMLとして書き出せるようにした。`IDialogService` に
  `ShowSaveBookmarkSettingsDialogAsync` を追加。
- **`0db8409` 結合前ページ数の編集を可能に** — `BookmarkNode.PageOffset`(int?)を新設し、
  `BookmarkNodeViewModel.PreOffsetPageNumber` の編集を `BookmarkTreeViewModel.OnPreOffsetPageNumberChanged`
  で同一ファイル内の後続ノードへ連鎖反映する仕組みを追加。
- **`8384e20` テキストボックスの自動幅調整・強調表示・リセットメニュー** —
  `PageNumberWidthConverter`・`EditedHighlightBrushConverter`(WPF/Avalonia双方)を追加。
- **`8a51402` ファイル単位でのページ番号リセット、v1.2.0へバージョンアップ** —
  `ResetFilePageNumbers(node)` を追加。個々のノード単位のリセットでは、リセット対象より前の
  ページへの過去編集が残ってしまう問題を、ファイル全体を一括で戻す設計にすることで解消した。

<a id="v121"></a>
## v1.2.1(2026-08-12)

3個のコミット。ユーザー報告に基づく2件の不具合修正と、コードレビューに基づく追加の堅牢化。

### `75296f1` 大量しおり時のUIフリーズ修正

`RecomputeAllPageNumberDisplaysAsync`(結合前ページ数編集のたびにツリー全体を2回走査する処理)は
完全に同期実行だったため、しおりが大量にある状態での編集・追加・削除・Undoのたびに、UIスレッドを
長時間占有し、`IsBusy` 相当の表示すら描画される機会がないままフリーズしたように見え、進捗も
一切更新されない不具合があった。対象ノード数が `RecomputeChunkSize`(200件)を超える場合のみ、
書き戻しループをチャンクに分割し `await Task.Yield()` でUIスレッドへ制御を返しながら
`IsBusy`/`BusyProgress` を更新するよう変更した。

この修正は、**v1.1.0で「ファイル読込・PDF結合」向けに導入されたbusyオーバーレイの枠組み
(`6db6849`)を、そのままバックグラウンド作業の種類が違う「しおりツリーの再計算」にも
転用した**という点で、v1.1.0の設計を踏襲している(専用のUIを新設せず、
`MainWindowViewModel.IsBusy`/`BusyProgress` への転送機構を追加しただけで済んでいる)。
詳細設計は [03-app-design.md §2.6](03-app-design.md#recompute) を参照。

### `0a231ab` しおり行クリック時の横スクロール位置維持

しおり編集ツリーの1行は横に長いため、横スクロールバー表示中に行をクリックすると、WPF・Avalonia
双方の既定の「対象を画面内へ収める」動作により横スクロール位置が意図せず動いてしまう不具合を修正。
クリック直後の横スクロール位置を保存し、選択・フォーカス変更処理が完了した後の低優先度タイミングで
復元する方式を採用した。**v1.1.0の `9805b89`(行ストレッチ案の差し戻し)で一度見送られた、
同種の横スクロール問題への再挑戦**にあたる。詳細設計は
[04-ui-design.md §2.3](04-ui-design.md#scroll-fix) を参照。

### `a7ee360` コードレビュー: UndoCommandのCanExecuteにもIsBusyを反映

上記のチャンク処理中は、処理中オーバーレイがマウス操作をブロックすることが主な防御だったが、
それだけに頼らず `UndoCommand` 自身のCanExecuteにも `!IsBusy` を組み込む多重防御を追加
(オーバーレイを経由しない将来の入力経路との競合を理論上も残さないため)。

---

<a id="v122"></a>
## v1.2.2(2026-08-17)

1個のコミット(`e044f98`)。ユーザー要望に基づく機能追加2件。

### しおり編集ツリーの開閉レベル一括指定コントロール

しおり編集画面のツリー直上に「-」ボタン・レベル指定テキストボックス・「+」ボタンを追加した。
`BookmarkTreeViewModel` に新設した `ExpandLevelInput`(string)へ数値Nを入力すると、
レベルN以下のノードは開いた状態(`IsExpanded=true`)、それを超えるノードは閉じた状態
(`IsExpanded=false`)になる(例: N=3ならレベル1〜3が開き、レベル4以降が閉じる)。
「-」ボタン(`CollapseAllCommand`)は`ExpandLevelInput`を`"0"`に、「+」ボタン
(`ExpandAllCommand`)はツリーの最大レベルに設定することで、同じ適用ロジックを再利用しつつ
全閉・全開を実現している。数値以外・ツリーに含まれない数値の入力は、テキストボックスが
フォーカスを失った際、またはしおり側の構造編集(追加・削除・移動・レベル上限切り詰め・読込・
元に戻す)によって現在値がツリーに含まれなくなった際に、`NormalizeExpandLevelInput` が
空文字へ正規化する。

**この一括適用処理(`ApplyExpandLevelAsync`)は、v1.2.1で導入された`RecomputeAllPageNumberDisplaysAsync`
と同じチャンク処理・`IsBusy`/`BusyProgress`の枠組みをそのまま再利用しており**、大量しおりの
ツリーで一括開閉してもUIがフリーズしない。詳細設計は
[03-app-design.md §2.7](03-app-design.md#expand-level) と
[04-ui-design.md §2.6](04-ui-design.md#expand-level-controls) を参照。

### 設定ダイアログにリリースバージョンを表示

`SettingsViewModel` に `AppVersion`(string)を追加し、設定ダイアログの右下に表示するように
した。`Assembly.GetExecutingAssembly()` の `AssemblyInformationalVersionAttribute` から取得する
値で、`Directory.Build.props` の `<Version>` がビルド時にそのまま書き込まれたものを使う
(WPF版・Avalonia版どちらも同じ`Directory.Build.props`を参照するため、App.dllのバージョンで
代表できる)。

---

<a id="v123"></a>
## v1.2.3(2026-08-18)

1個のコミット(`0e593eb`)。ユーザー報告に基づく不具合修正1件。
(なお `v1.2.2` タグと `v1.2.3` タグの間には、v1.2.2のドキュメント同期作業の一部だった
`97b4df2` によるコメント修正(`ConverterParityTests.cs` の参照先を
`design.html` → `docs/ja/04-ui-design.html` へ更新、動作に影響なし)も含まれる。)

### `0e593eb` PDF結合後、ページ内リンクのジャンプ先が壊れる不具合の修正

複数PDFを結合すると、各PDF内に元々あったページ内リンク(GoTo型の内部リンク)のジャンプ先が
正しくなくなる不具合が報告された。原因はPDFsharpの `AddPage` にある。ページと注釈自体
(`/Subtype /Link`)は複製するが、しおりと異なり、リンクの `/Dest` や `/A`(GoToアクション)の
`/D` が参照するページオブジェクトまでは結合後のものに書き換えない。実際に再現用のテストPDFで
確認したところ、結合後のリンクは結合結果内に存在しないオブジェクトを指す(ダングリング参照になる)
ことが確かめられた。

修正は `ApplyBookmarks` が使っている `pageMap`(`(SourceFileEntryId, OriginalPageIndex)` → 実ページ)
と同じ考え方を、`PdfMergeService` のページ結合ループに追加する形で行った。ファイルごとに
「ソースページオブジェクトのID → 元ページ番号」の対応表(`sourcePageIndexByObjectId`)を構築し、
各ページ内リンク注釈の `/Dest`・`/A/D` が指す元ページ番号を特定した上で、`pageMap` で結合後の
実ページに解決し、コピー済み注釈の配列先頭要素(ページ参照)を直接書き換える。名前付きジャンプ先
(`/Dest` が名前・文字列で表現されるもの)は対象外。詳細設計は
[02-core-design.md §2.5](02-core-design.md#link-remap) を参照。

回帰テストとして、`/Dest` に直接ページ参照を持つ形式と、`/A`(GoToアクション)の `/D` にページ参照を
持つ形式の両方について、結合後のジャンプ先が正しい結合後ページを指すことを検証するテストを追加した
(`PdfMergeServiceTests`)。

---

<a id="v130"></a>
## v1.3.0(2026-08-20)

10個のコミット。「リンク編集」機能(結合・しおり設定済みのPDFをプレビューしながら、本文中の
テキストを選択してリンクを作成・確認・削除する新画面)の追加。
(なお `v1.2.3` タグと `v1.3.0` タグの間には、v1.2.3自身のドキュメント同期作業だった
`e964b6f` も含まれる。)

### 基盤(`a885e2`〜`c6230b`)

- **`a885e26` PDF描画・テキスト抽出サービスの追加** — PDFsharpにはページのラスタライズ機能も
  位置情報付きテキスト抽出機能も無いため、Core層へ2つの新規依存を追加した:
  `PDFtoImage`(PDFiumラッパー、macOS arm64含め積極的にメンテナンスされている)でページを
  PNGへ描画する `PdfPageRenderer`、`UglyToad.PdfPig`(純粋管理コード)で文字単位の矩形を
  抽出する `PdfTextExtractor`。`PdfPageRenderer` は文書ハンドルを保持せずページごとに
  ステートレスに描画する設計とした — `PDFtoImage` の公開APIにそもそも再利用可能なハンドルが
  無いこと、2000ページ級PDFでのベンチマークでページ位置に関わらず1ページ16〜26msで安定して
  いたことから、ハンドル使い回しの複雑さに見合わないと判断した。`PdfTextLetter`/`PdfRect`
  などのデータモデルもここで追加。
- **`c6230ba` `PdfLinkAnnotationService`(書き込み)の追加** — 既に結合・しおり設定済みのPDFへ、
  `PdfDocumentOpenMode.Modify` で開いて既存の `PdfPage` オブジェクトへ直接 `/Annots` を追加する
  方式で最終出力する。`AddPage` によるページ再構築を経由すると、[v1.2.3](#v123) で修正した
  内部リンクのジャンプ先破損と同種の問題を再度招く恐れがあるため、意図的に避けている。

### 画面とリンク作成ロジック(`1f0827`〜`ac90d8`)

- **`1f08273` リンク編集画面の骨格追加** — `WorkflowStep.EditLinks` を新設し、結合成功後の遷移先を
  ここへ変更。`LinkEditorViewModel` にページ送り・ズーム・しおり読み込み(読み取り専用)を実装。
- **`d559ebc` リンク作成ロジックの追加** — `PdfCoordinateMapper`(PDFユーザー空間⇔ビットマップ
  ピクセル座標の相互変換)、文字単位のドラッグ選択によるヒットテスト、複数行選択時の行ごとの
  矩形分割を実装。実装中に、`PdfCoordinateMapper.ToPixelRect` が `PdfRect` を誤った位置引数順で
  構築しTop/Bottomが入れ替わるバグと、ページ描画とテキスト抽出を独立した2つの非同期チェーンに
  していたことで `IsBusy` と `CurrentPageIndex` の同期が崩れる本物のflakyテスト failureを、
  それぞれ実装中に発見・修正した。
- **`ac90d87` リンク作成UIの配線** — プレビュー上のポインタ操作から選択矩形のライブ表示・
  リンクのホットスポットオーバーレイ描画までを配線。しおりサイドバーを、選択確定後のジャンプ先
  ピッカーとしても再利用する設計にした。

### リンク管理・完了・既存リンク(`6949df`〜`6bdb03`)

- **`6949df4` リンク管理UIの追加** — `LinkGroups`(GroupIdごとの集約ビュー)の一覧表示・
  ジャンプ・編集・削除を実装。
- **`5e4a239` 完了コマンドの追加** — `FinishAsync` は、`LoadAsync` 時に取得した「素の状態
  (リンク注釈が一切無い時点)」のバックアップから毎回復元してから `ApplyLinksAsync` を呼ぶことで、
  「完了」を複数回押しても注釈が重複しない冪等性を確保している(`PdfLinkAnnotationService` が
  追記専用で削除・置換ができないための設計)。
- **`6bdb033` 既存リンク注釈の読み取り追加(Core層)** — `ReadExistingLinksAsync` で、PDFに元から
  含まれる `/Subtype /Link` 注釈を `LinkAnnotationNode` として読み取れるようにした。書き込みと
  同じ低レベルAPIで実装し、書き込み→読み取りのラウンドトリップをテストで検証している。

### 連続スクロールへの刷新と手動テストでの不具合修正(`1e075c`)

- **`1e075c5` プレビューを連続スクロール方式へ刷新、クロススレッドクラッシュ修正、手動テスト
  フィードバックへの対応** — WPF実機での手動テストで見つかった実際の不具合2件を含む大きな
  イテレーション。
  1. **クロススレッドクラッシュ**: `LinkEditorViewModel` の非同期メソッドが
     `ConfigureAwait(false)` を使っていたため、最初の `await` 以降の継続処理がスレッドプール
     スレッドで実行され、その中で `ReactivePropertySlim<T>.Value`(WPFの`CommandManager`が
     UIスレッド専用で監視)を書き換えた瞬間に `InvalidOperationException` が発生していた。
     `ConfigureAwait(false)` を全箇所で除去し解決(App層ViewModelはこれを使わないという
     既存の規約に合わせた)。詳細は [01-architecture.md §4.5](01-architecture.md#cross-cutting)。
  2. **連続スクロールプレビューへの刷新**: 単一ページ+ピーク画像方式を廃し、`PageSlots`
     (仮想化された `ListBox` にバインドする軽量プレースホルダの列)方式へ全面刷新。
     スクロール中は「ビューポート内で最も表示面積が大きいページ」を`CurrentPageIndex`とする。
     この過程で、`Wpf.Ui`の`FluentWindow`が内部の`ScrollViewer`を独自の`PassiveScrollViewer`へ
     差し替えておりヒットテストが機能しない問題と、`ListBox.ScrollIntoView`がページの先頭ではなく
     末尾に揃ってしまう問題という、2つの非自明なレイアウトバグを発見・修正した。詳細は
     [04-ui-design.md §6.2](04-ui-design.md#link-editor-scroll-fix)。
  3. その他、既存リンクの一覧表示配線・ボタンスタイルの統一・ダークモードでのしおり
     `TreeView`文字色修正など、手動テストで見つかった小さな修正を多数含む。

### `0f1b476` 設計ドキュメントの更新

全6章・日英両言語・Markdown/HTML両形式について、リンク編集機能に関する記述を追加。

---

<a id="v131"></a>
## v1.3.1(2026-08-20)

3個のコミット。v1.3.0リリース後のコードレビューフォローアップで見つかった、設定値の複製・
大量しおり時のフリーズ・リンク編集画面の選択操作という3系統の不具合修正。

### `c7b14e1` 設定値クローンの脆弱性修正

`MainWindowViewModel.MergeCoreAsync` と `SettingsViewModel.ToOptions` がそれぞれ独立に
`PdfBookmarkMergerOptions` の全プロパティを手書きで列挙してコピーしていたため、新しいプロパティを
追加した際に片方だけ追従し忘れる不具合が実際に発生していた(`ShowMergeAndEditLinksButton` が
結合のたびに既定値へ静かに巻き戻るバグ)。`PdfBookmarkMergerOptions.Clone()` を新設し、両呼び出し
箇所をこれ経由に統一。全公開プロパティが `Clone()` を生き残ることをリフレクションで検証する
回帰テストと、両呼び出し箇所の実際の挙動を検証するテストを追加した。

### `1baa944` 大量しおりツリーの読込・元に戻す時の長時間フリーズ修正

`BookmarkTreeViewModel.RebuildTree`(読込・Undoの共通経路)は、`BookmarkNodeViewModel`の
コンストラクタが子孫全ノード分のViewModelを再帰的に構築する設計だったため(1ノードあたり
Rx購読が約12本)、約2000ノードのツリーで1分近く無停止で走り、busyオーバーレイの初回描画すら
間に合わないほどのフリーズを起こしていた。コンストラクタから再帰構築を除去し、
`BookmarkTreeViewModel.RebuildTreeAsync`(旧称`RebuildTree`)が`RecomputeChunkSize`(200件)
ノードごとに`await Task.Yield()`で制御を返しながら深さ優先で構築する方式へ変更、
[v1.2.1](#v121)で導入済みの`IsBusy`/`BusyProgress`チャンク処理の枠組みを再利用した。
`Load`/`Undo`は`LoadAsync`/`UndoAsync`へ改名されている。詳細設計は
[03-app-design.md §2.6.1](03-app-design.md#rebuild-tree-async)を参照。

### `d11f72f` リンク編集画面の選択操作・プレビュー表示の不具合修正

WPF/Avalonia双方での手動テストフィードバックに基づく4件の関連修正。

1. 任意の位置へのジャンプ先指定中にページをまたいでスクロールすると選択状態がリセットされて
   しまい、本質的にページ横断が前提の操作であるこの機能がほぼ使えなくなっていた不具合を修正
   (`LoadCurrentPageMetadataAsync`のリセット範囲を、ドラッグ中の一時状態のみへ縮小)。
2. 矩形選択のドラッグ終了直後に選択範囲の可視化が消えてしまう不具合を修正
   (`LiveSelectionLineRects`によるドラッグ中表示と`PendingSelection`によるドラッグ後の保持表示を
   統合し、確定済みリンク(緑)と区別可能な色(青)で描画)。
3. リンク編集・保存後にファイル選択へ戻って再結合し、再度リンク編集画面へ入ると、プレビューの
   スクロール可能範囲が前回ファイルのページ数のまま固定される不具合を修正
   (WPFの`VirtualizingStackPanel`内部キャッシュの問題。`LinkEditorViewModel.LoadGeneration`の
   変更を契機に`VirtualizingPanel.IsVirtualizing`を一度オフ→オンへ切り替えて内部状態を再構築)。
4. 既存リンクの一覧を現在プレビュー中のページ分のみに絞り込み、非アクティブなページを半透明の
   グレーで重ね書きして視覚的に区別できるようにした。

詳細設計は[03-app-design.md §7.3〜7.4、§7.7](03-app-design.md#link-editor)、
[04-ui-design.md §6.1〜6.4](04-ui-design.md#link-editor-ui)を参照。

---

## 確認方法

以下のコマンドを実行すると、本ドキュメントの記述を自分で再確認できる。

```bash
# タグ一覧・各タグのコミット日時
git tag -l --sort=v:refname
git log -1 --format="%ad %s" --date=short v1.1.0

# バージョン間で変更されたファイル一覧
git diff --name-status v1.0.0 v1.1.0 -- src tests
git diff --name-status v1.1.0 v1.2.0 -- src tests
git diff --name-status v1.2.0 v1.2.1 -- src tests
git diff --name-status v1.2.1 v1.2.2 -- src tests
git diff --name-status v1.2.2 v1.2.3 -- src tests
git diff --name-status v1.2.3 v1.3.0 -- src tests
git diff --name-status v1.3.0 v1.3.1 -- src tests

# バージョン間の全コミット(区間内の個別コミットメッセージ)
git log --oneline v1.0.0..v1.1.0 -- src tests
git log --oneline v1.1.0..v1.2.0 -- src tests
git log --oneline v1.2.0..v1.2.1 -- src tests
git log --oneline v1.2.1..v1.2.2 -- src tests
git log --oneline v1.2.2..v1.2.3 -- src tests
git log --oneline v1.2.3..v1.3.0 -- src tests
git log --oneline v1.3.0..v1.3.1 -- src tests

# 個別コミットの詳細diff
git show <コミットハッシュ>

# 現在のテスト件数(プロジェクトごと)
dotnet test tests/PdfBookmarkMerger.Core.Tests/PdfBookmarkMerger.Core.Tests.csproj --list-tests
dotnet test tests/PdfBookmarkMerger.App.Tests/PdfBookmarkMerger.App.Tests.csproj --list-tests
dotnet test tests/PdfBookmarkMerger.UiConverters.Tests/PdfBookmarkMerger.UiConverters.Tests.csproj --list-tests
```
