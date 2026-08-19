# 03. App層設計

`PdfBookmarkMerger.App` はUIフレームワークに依存しないViewModel層・アプリ共通サービス層。
`ServiceCollectionExtensions.AddPdfBookmarkMergerApp()` で `IUserSettingsService` /
`FileListViewModel` / `BookmarkTreeViewModel` / `MainWindowViewModel` をSingleton登録する。

いずれのViewModelも `ViewModelBase`(`CompositeDisposable Disposables` を持つだけの薄い基底クラス)
を継承し、[Reactive.Bindings](https://github.com/runceel/ReactiveProperty) の
`ReactivePropertySlim<T>` / `ReactiveCommand` / `AsyncReactiveCommand` でプロパティ・コマンドを
公開する。

## 1. `MainWindowViewModel`

メインウィンドウ全体を統括し、`Step`(`WorkflowStep.SelectFiles` → `EditBookmarks` →
(任意)`EditLinks`)の遷移と主要コマンドを管理する。

| コマンド | CanExecute | 処理 |
|---|---|---|
| `ConfirmFilesCommand` | `HasFiles && !IsBusy` | 全ファイルのメタデータを並列読込 → しおり抽出・結合後ページ番号計算 → `BookmarkTree.Load` → `Step = EditBookmarks` |
| `MergeCommand` | `(Step==EditBookmarks) && !IsBusy && !HasPageNumberEdits` | `MergeCoreAsync(continueToLinkEditing: false)`。結合してここで手順を終える |
| `MergeAndEditLinksCommand` | 同上 | `MergeCoreAsync(continueToLinkEditing: true)`。結合後 `LinkEditor.LoadAsync` → `Step = EditLinks` |
| `SaveBookmarkSettingsCommand` | `(Step==EditBookmarks) && !IsBusy && !HasPageNumberInconsistency` | 保存先ダイアログ→`BookmarkSettingsExportService.ExportAsync` |
| `BackToFileListCommand` | `(Step==EditBookmarks) && !IsBusy` | `Step = SelectFiles` に戻る |
| `BackToBookmarksCommand` | `(Step==EditLinks) && !IsBusy` | `Step = EditBookmarks` に戻る(生成済みの中間ファイルはそのまま残る) |
| `FinishLinkEditingCommand` | `(Step==EditLinks) && !IsBusy` | `LinkEditor.FinishAsync()` → 完了ダイアログ |

`MergeCommand`/`MergeAndEditLinksCommand`はいずれも保存先ダイアログ→(設定により)
プロパティ編集ダイアログ→`PdfMergeService.MergeAsync`という結合処理自体を共有する
`MergeCoreAsync(bool continueToLinkEditing)`の2つの入口。`MergeAndEditLinksCommand`(=
「結合してリンク編集へ進む」ボタン)は、設定の`ShowMergeAndEditLinksButton`(既定false、
設定ファイル未読み込み時も既定false)が有効な場合のみUI上に表示される
(`MainWindowViewModel.ShowMergeAndEditLinksButton`、設定ダイアログでOKした時点で即座に反映)。

`ConfirmFilesAsync` は各ファイルのメタデータ読込を `SemaphoreSlim`(上限
`Math.Clamp(Environment.ProcessorCount, 1, 8)`)で並列化し、`Task.WhenEach` で完了順に結果を
反映する。一部ファイルの読込失敗は `PdfFileEntryViewModel.LoadFailed` へ記録され、
以後のしおりツリー構築・実際のPDF結合(`MergeAsync`)の**両方**から一貫して除外される
(過去の回帰: しおりツリーからは除外されるが結合対象には残ってしまうバグの再発防止テストあり)。

`MergeCommand` が `!HasPageNumberEdits` を要求するのは、結合前ページ数編集中は結合後PDFの
実際のページ位置と画面表示・書き出し内容が食い違うため。`SaveBookmarkSettingsCommand` が
`!HasPageNumberInconsistency` を要求するのは、編集の結果ページ数が1未満になるような不整合が
起きている場合、正しいXMLを書き出せないため(`HasPageNumberEdits`/`HasPageNumberInconsistency`は
`BookmarkTreeViewModel` 側で管理、後述)。

### 1.1 busy / progress の転送(v1.2.1〜)

```csharp
BookmarkTree.IsBusy.Subscribe(busy => { ...; IsBusy.Value = busy; });
BookmarkTree.BusyProgress.Subscribe(p => BusyProgress.Value = p);
```

コンストラクタでこの2行を購読することで、`BookmarkTreeViewModel` 内部の大量ノード再計算
(後述 [§2.6](#recompute))による busy状態を、ファイル読込・PDF結合と同じ `IsBusy`/`BusyProgress`
経由でUI(処理中オーバーレイ)へそのまま反映する。busy開始時は現在の `StatusMessage` を退避して
「しおりの情報を更新しています…」に差し替え、終了時に復元する。

## 2. `BookmarkTreeViewModel`

しおり編集ツリー(手順2/3)の中心ViewModel。D&Dによる並べ替え・再親子付け、追加・削除・
レベル操作、タイトル等の編集結果を `Core.Models.BookmarkNode` ツリー(`_rootModel`)へ同期する。

### 2.1 公開プロパティ

| プロパティ | 型 | 役割 |
|---|---|---|
| `RootNodes` | `ObservableCollection<BookmarkNodeViewModel>` | ルート直下のノード一覧(UIバインド対象) |
| `ForceFitForAll` | `ReactivePropertySlim<bool>` | オンの間、全ノードの表示方法・座標コントロールを不活性化し結合時は一律Fit扱い(個々の設定値自体は変更しない) |
| `GlobalExpandOverride` | `ReactivePropertySlim<bool?>` | 「一律で展開表示を設定」の3状態(true=全展開/false=全収納/null=個別設定に従う) |
| `CanUndo` / `UndoCommand` | — | 後述 [§2.4](#undo) |
| `HasPageNumberEdits` / `HasPageNumberInconsistency` | `ReactivePropertySlim<bool>` | 結合前ページ数編集の状態(`MainWindowViewModel` のCanExecuteへ伝播) |
| `IsBusy` / `BusyProgress` | `ReactivePropertySlim<bool>` / `ReactivePropertySlim<BusyProgressInfo?>` | 後述 [§2.6](#recompute) |
| `TitleColumnBaseWidth` | `ReactivePropertySlim<double>` | タイトル列の共有基準幅。実測はUI側(`MainWindow.xaml.cs`等)が行う |
| `ExpandLevelInput` / `CollapseAllCommand` / `ExpandAllCommand` | — | 後述 [§2.7](#expand-level) |

### 2.2 構造編集(追加・削除・移動・レベル操作)

`AddRoot` / `AddChild` / `AddSiblingAfter` / `Remove` / `Move` はいずれも「Undoスナップショットを
積む → `RootNodes`(UI用ObservableCollection)と `_rootModel`(Coreモデル)の両方を更新 →
`TriggerRecompute()`」という共通パターンを踏む。`Move` は同一コレクション内移動時のインデックス
補正、および移動先が自身の子孫でないことの検証(`IsDescendantOf`)を行う。

`PromoteLevel`/`DemoteLevel` は実体としてはいずれも `Move` の特殊形(親の直後の兄弟へ/直前の
兄弟の末尾の子へ、それぞれ再配置)として実装されている。

`SetChildLevelCapAsync(node)` はダイアログで選択された絶対レベルより深い下位要素を
`TruncateBelowLevel` で一括削除する。

新規追加ノードには、その時点で有効な「一律でFitに設定」「一律で展開表示を設定」の上書きを
即座に適用する(`ApplyCurrentOverridesToNewNode`)。この適用自体は追加操作の一部として扱われ、
独立したUndoスナップショットにはならない(`_suppressUndoSnapshots` で抑止)。

### 2.3 結合前ページ数編集

`BookmarkNodeViewModel.PreOffsetPageNumber` が編集されると
`BookmarkTreeViewModel.OnPreOffsetPageNumberChanged(node, newValue)` が呼ばれ、

1. 差分 `delta = newValue - 編集前の実効値` を求める(0なら何もしない)。
2. 同一ファイル(`SourceFileEntryId`)内で、編集されたノードの `OriginalPageIndex` **以降**
   (ツリー上の表示順ではなく、抽出元PDFのページ構造上の位置基準)にある全ノードの
   `PageOffset` へ一律加算する。
3. `TriggerRecompute()` を呼ぶ。

`ResetFilePageNumbers(node)` は、個々のノード単位のリセット(そのノード以降のみ戻す)では
リセット対象より前のページへの過去編集が残ってしまうため、**同一ファイル全体**の `PageOffset` を
一括で `null` へ戻す。そのファイルに編集が1件もなければ何もせずUndo履歴も積まない。

結合後ページ数への反映は `ComputeCumulativeDeltaBeforeFile()` が担う。ファイルごとに
「そのファイル内で最も `OriginalPageIndex` が大きいノードの `PageOffset`」(=そのファイル全体に
効く累積差分。どの編集も自身の位置以降=最終ページを含む範囲に及ぶため)を求め、結合順
(`_orderedFileIds`)に沿って積算することで、各ファイルについて「自分より前のファイルの
累積差分の合計」を得る。この関数は `RecomputeAllPageNumberDisplaysAsync` と `ToExportModel` の
両方から呼ばれる、読み取り専用の集計処理(チャンク分割はしていない。プロパティ書き戻しを
伴わないため実測負荷は書き戻しループよりずっと小さい)。

<a id="undo"></a>
### 2.4 Undo

`App/Undo/UndoHistory<T>` は、スナップショットの推定サイズ(バイト)を積算し、合計が上限
(既定100MB)を超えたら最新の1件を除き最古から破棄する、メモリ量ベースのスタック。
`BookmarkTreeViewModel` はこれを `string`(`_rootModel` のJSONシリアライズ)特化で使用する。

- `PushUndoSnapshot()`(引数なし) — 構造操作の直前に呼ぶ、常に1件積むコアレスなし版。
- `PushUndoSnapshot(coalesceKey)` — プロパティ編集用。同一キーへの連続呼び出しが800ms
  (`SnapshotCoalesceWindow`)以内なら1回の編集とみなし、履歴を積み増さない
  (テキスト入力中の1文字ごとの履歴増殖を防ぐ)。
- `Undo()` — 最新スナップショットをJSONデシリアライズし `RebuildTree` へ渡す(履歴自体は
  Popされ消費される。LIFO順)。

`BookmarkNodeViewModel` の各プロパティ(`Title`/`IsOpen`/`DestinationType`/座標)は、構築時の
初回リプレイ値を `Skip(1)` で除外した上で変更ごとに `RequestUndoSnapshot` を呼ぶ。「一律で
Fitに設定」「一律で展開表示を設定」の上書き適用自体は表示上の一時的な変更(オフに戻すと自動復元)
でありUndo対象の「編集内容」ではないため、`_suppressUndoSnapshots` で抑止する。

### 2.5 `CanUndo`/`UndoCommand` のCanExecute(v1.2.1〜)

```csharp
var canUndo = CanUndo.CombineLatest(IsBusy, (canUndo, busy) => canUndo && !busy);
UndoCommand = new ReactiveCommand(canUndo);
```

大量ノードのチャンク処理中(`IsBusy`)は、処理中オーバーレイがマウス操作をブロックすることが
主な防御だが、それだけに頼らず `UndoCommand` 自身のCanExecuteにも `!IsBusy` を組み込んでいる
(オーバーレイを経由しない将来の入力経路、例えばキーボードショートカット等から進行中の
再計算と競合する余地を理論上残さないための多重防御。コードレビューで追加)。

<a id="recompute"></a>
### 2.6 大量しおり時の応答性 — チャンク処理(v1.2.1〜)

<img src="../diagrams/recompute-flow.svg" alt="チャンク再計算とbusyオーバーレイのフロー" width="100%" />

`RecomputeAllPageNumberDisplaysAsync()`(旧称 `RecomputeAllPageNumberDisplays`、内部
`_isRecomputingPageNumbers` フラグで自己書き戻しからの再帰を防ぐ)は、ツリー構造・`PageOffset`
を変更しうるすべての操作(読込・Undo・追加・削除・レベル上限切り詰め・編集)の後に呼ばれ、
全ノードの `PreOffsetPageNumber`/`DisplayMergedPageNumber`/`IsPageNumberEdited` を再計算して
書き戻し、`HasPageNumberEdits`/`HasPageNumberInconsistency` を更新する。

対象ノード数が `RecomputeChunkSize`(200件)を超える場合のみ、書き戻しループを200件ごとの
チャンクに分割し、各チャンクの合間に `await Task.Yield()` でUIスレッドへ制御を返す。この間
`IsBusy`/`BusyProgress` を更新し、`MainWindowViewModel` が自身の同名プロパティへ転送することで、
専用UIを新設せずファイル読込時と同じ処理中オーバーレイ・進捗表示を再利用する。200件以下の
小規模なツリーでは内部で一度も `await` が発生せず、これまでどおり完全に同期的に完了する
(小規模ツリーでの不要なオーバーヘッド・ちらつきを避けるため)。

構造編集メソッド群(`AddRoot`等)は同期メソッドのままシグネチャを変えずに済ませるため、
`TriggerRecompute()`(`async void` のfire-and-forgetラッパー)を経由して呼び出す。

```csharp
private async void TriggerRecompute() => await RecomputeAllPageNumberDisplaysAsync();
```

小規模ツリーでは内部で一度もawaitしないため、このメソッドから戻った時点で実質的に処理は
完了しており、既存の同期呼び出し前提のテスト・コードビハインドは無改修で動作する。大規模ツリーでは
処理中オーバーレイがしおり編集画面全体を覆いマウス操作を受け付けなくなるため、実行中に
本メソッドの別呼び出しが(ユーザー操作起点で)重ねて発生することは想定していない。

<a id="expand-level"></a>
### 2.7 ツリー開閉レベルの一括指定(v1.2.2〜)

しおり編集ツリー直上の「-」ボタン・レベル指定テキストボックス・「+」ボタンを支えるロジック。

- `ExpandLevelInput`(`ReactivePropertySlim<string>`) — テキストボックスと双方向バインドする。
  値が有効な数値(0以上、現在のツリーに含まれる最大レベル以下の整数)に変わるたびに、その数値
  以下のレベルのノードを `IsExpanded=true`、それを超えるノードを `IsExpanded=false` に変更する
  (例: `"3"` ならレベル1〜3が開き、レベル4以降が閉じる)。適用処理自体(`ApplyExpandLevelAsync`)は
  [§2.6](#recompute) の `RecomputeAllPageNumberDisplaysAsync` と同じチャンク処理・
  `IsBusy`/`BusyProgress` の枠組みをそのまま再利用しており、大量しおりのツリーでも一括開閉で
  UIがフリーズしない。
- `CollapseAllCommand`(「-」ボタン) — `ExpandLevelInput` を `"0"` に設定する(全ノードのレベルは
  1以上のため、`LevelNumber &lt;= 0` は常に偽になり全ノードが閉じる)。
- `ExpandAllCommand`(「+」ボタン) — `ExpandLevelInput` をツリーの現在の最大レベルに設定する
  (全ノードの `LevelNumber` が必ずその値以下になるため全ノードが開く)。両コマンドとも
  `UndoCommand` と同様 `!IsBusy` をCanExecuteに含める。
- `NormalizeExpandLevelInput()`(public) — 数値以外、またはツリーに含まれない数値(現在の
  最大レベルを超える値)が入力された場合に空文字へ正規化する。テキストボックスのLostFocus時に
  コードビハインドから呼ばれるほか、`AddRoot`/`AddChild`/`AddSiblingAfter`/`Remove`/`Move`/
  `SetChildLevelCapAsync`/読込・元に戻す各操作の内部からも呼ばれ、しおり側の構造編集によって
  現在値がツリーに含まれなくなった場合も自動的に空へ戻す。

## 3. `BookmarkNodeViewModel`

しおりツリーの1ノード分のViewModel。`Title`/`IsOpen`/`DestinationType`/`Left`/`Top`/`Right`/`Bottom`/`Zoom`
の各 `ReactivePropertySlim<T>` は、値が変わるたびに対応する `Model`(`BookmarkNode`)へ直接
反映しつつ、`BookmarkTreeViewModel` へUndoスナップショット要求を送る。構築時の初回リプレイ
(`ReactivePropertySlim.Subscribe` は購読直後に現在値を1回リプレイする)を `Skip(1)` で除外しないと、
ノード生成のたびに実際の変更なしでUndo履歴が積まれてしまう(Undo自体がツリーを再構築するため、
無限増殖するバグになる)。

`PreOffsetPageNumber` だけは他プロパティと異なり `Model` へ直接反映しない。編集後の値を
どのノードへどう波及させるか(同一ファイル内の後続・後続ファイルへの結合後ページ数の連鎖)は
`BookmarkTreeViewModel` 側でまとめて計算する必要があるため、変更通知のみを上位へ渡す。

`IsDestinationTypeEditable`/`IsLeftEditable`/`IsTopEditable`/`IsZoomEditable` は、選択中の
表示方法(`XYZ`/`Fit`/`FitH`/`FitV`)に応じて実際にPDFへ反映される座標コントロールのみを
活性化する派生プロパティ(`ForceFitForAll` がオンの間はすべて不活性化)。

## 4. `FileListViewModel`

結合対象PDFファイル一覧(手順1)。D&D/ダイアログでの追加(`AddPaths`)、選択ブロック単位の
上下移動(`MoveSelectionUp`/`MoveSelectionDown`、選択が連続していない場合は何もしない)、
D&D並べ替え(`MoveTo`)を扱う。`GetMoveAvailability` は選択が空または非連続なら両ボタンとも
非活性を返す。

## 5. サービス

| 型 | 役割 |
|---|---|
| `IDialogService`(実装はWpf/Avalonia側) | ファイル/フォルダ選択・保存・プロパティ/設定/レベル上限ダイアログの表示 |
| `IUserSettingsService` / `UserSettingsService` | ユーザー設定の読み取り(`IOptionsMonitor` 経由)・原子的な保存 |
| `AppLanguageBootstrapper` | 起動時の表示言語確定・設定ダイアログでの即時切替 |
| `AppPaths` | 設定・ログの保存先パス解決 |

## 6. 補助的なViewModel

`PdfFileEntryViewModel`(ファイル一覧の1行、`PageCount`/`LoadFailed` を保持)、
`PropertiesDialogViewModel`・`SettingsViewModel`・`LevelCapDialogViewModel`(各ダイアログの
入力値をViewModel化したもの、WPF/Avalonia共通で `IDialogService` の実装から生成・破棄される)。
`SettingsViewModel` は **v1.2.2〜**、設定ダイアログに表示する `AppVersion`(string)も公開する。
`Assembly.GetExecutingAssembly()` の `AssemblyInformationalVersionAttribute` から取得した値で、
`Directory.Build.props` の `<Version>` がビルド時にそのまま書き込まれたものを使う。
`PdfPageSlotViewModel`(連続スクロールプレビューの1ページ分のプレースホルダ)は
[§7.2](#link-editor-scroll)を参照。
`ShowMergeAndEditLinksButton`(`ReactivePropertySlim<bool>`)は「結合してリンク編集へ進む」
ボタンの表示・非表示を切り替える設定。`ThemeMode`/`Language`と異なり、テーマ再適用や
ウィンドウ再構築を一切伴わない単純なバインディング先の値なので、`MainWindowViewModel.OpenSettingsAsync`
がダイアログ確定直後にこの値を書き換えるだけでXAML側のバインディングが即座に追従する。

<a id="link-editor"></a>
## 7. `LinkEditorViewModel`

手順5(`WorkflowStep.EditLinks`)を統括するViewModel。結合・しおり設定済みの単一PDFファイルを
対象に、連続スクロールのページプレビュー・拡大縮小・しおり一覧からのジャンプ・文字選択による
リンク作成・リンクの一覧/確認/削除を扱う。`MainWindowViewModel`と同じ位置付けでDI登録される
(コンストラクタ引数: `IPdfPageRenderer`, `IPdfTextExtractor`, `IPdfMetadataService`,
`IPdfLinkAnnotationService`, `ILogger<LinkEditorViewModel>`)。

### 7.1 `LoadAsync` — 画面遷移時の初期化

`LoadAsync(filePath, ct)` は以下を行う。

1. `IPdfMetadataService.ReadMetadataAsync` でページ数・しおり一覧を取得する
   (結合済みファイルは「しおりを持つ1つのPDF」として扱えるため、複数ファイル対応特有の
   `SourceFileEntryId`/`OriginalPageIndex`は不要)。
2. 先頭ページ(0ページ目)のPDFユーザー空間サイズを取得し、連続スクロール表示の
   プレースホルダサイズ計算に使う([§7.2](#link-editor-scroll)参照)。
3. `PageSlots`(後述)をページ数分だけ生成する。
4. ファイルが実在する場合のみ(単体テストではフィクションのパスを渡すことがあるため)、
   一時フォルダへ「素の状態」のバックアップを作成し([§7.5](#link-editor-finish)参照)、
   `IPdfLinkAnnotationService.ReadExistingLinksAsync`で既存リンクを読み取って`Links`へ追加する
   ([§7.6](#link-editor-existing-links)参照)。

<a id="link-editor-scroll"></a>
### 7.2 連続スクロールプレビュー — `PageSlots` と仮想化

数千ページ規模のPDFでも全ページのビットマップを同時に保持しないよう、プレビューは
「軽量なプレースホルダの仮想化リスト」として設計されている。

- `PageSlots`(`ObservableCollection<PdfPageSlotViewModel>`) — 1ページにつき1つ、`PageIndex`・
  `Image`(`byte[]?`、未描画はnull)・`IsCurrent`(bool、選択・リンク作成・ホットスポット表示の
  対象かどうか)を持つ軽量なプレースホルダ。`LoadAsync`でページ数分だけ生成する
  (レンダリングは伴わない)。
- `LoadPageSlotAsync(pageIndex)` — UI側(仮想化パネルのコンテナが実体化された時)から呼ばれる。
  該当スロットが未描画ならページを描画して`Image`へ反映する。ページ単位の
  `CancellationTokenSource`辞書で、同じページへの重複呼び出し・追い越しを処理する。
- `UnloadPageSlot(pageIndex)` — コンテナがビューポートから外れた(仮想化パネルにより
  リサイクルされた)時に呼ばれる。描画中なら打ち切り、`Image`を`null`に戻してメモリを解放する。
  これが数千ページ規模のPDFでもメモリを抑えられる要。
- `PlaceholderWidth`/`PlaceholderHeight` — 未描画スロットの領域確保に使う、現在のズーム倍率での
  px幅・高さ。先頭ページのPDFユーザー空間サイズを全ページで代用する(ページごとの実サイズ取得は
  大規模PDFで高コストなため)。ズーム変更のたびに`PdfCoordinateMapper.PixelsPerPoint(scale)`で
  再計算する。

UI側(WPF: 仮想化`ListBox`、Avalonia: 同様)がスクロール位置から「ビューポート内で最も表示面積が
大きいページ」を求め`CurrentPageIndex`へ反映する仕組みの詳細は
[04-ui-design.md §6.2](04-ui-design.md#link-editor-scroll-ui) を参照。

### 7.3 `CurrentPageIndex` と `PageNumberInput` の同期

`CurrentPageIndex`(0始まり)が変わるたびに、`OnCurrentPageIndexChanged`が

1. 範囲外の値を最も近い有効な値へ丸める(自己再入。`ReactivePropertySlim`は値が変化しない限り
   再通知しないため収束する)。
2. 旧`_currentSlot`の`IsCurrent`をfalseへ、新しいスロットのそれをtrueへ切り替える。
3. `PageNumberInput`(1始まり、ページ送りツールバーのテキストボックスと双方向バインド)を同期する。
4. 現在ページのメタデータ(`PageHeight`・`Letters`、ページが変わった場合のみ文字抽出)を
   fire-and-forgetで取得する(`TriggerLoadCurrentPageMetadata`。ページのビットマップ自体は
   `PageSlots`の担当のため、ここでは扱わない)。

`PageNumberInput`側の変更(テキストボックスへの直接入力)も、範囲を丸めつつ
`CurrentPageIndex`へ反映する双方向の同期になっている(`OnPageNumberInputChanged`)。

### 7.4 文字選択によるリンク作成

- `BeginTextSelection`/`UpdateTextSelection`/`EndTextSelection` — ドラッグ開始・移動・終了の
  PDFユーザー空間座標を受け取り、現在ページの`Letters`(`PdfTextExtractor`が抽出した文字矩形の列)
  に対しヒットテストして選択範囲(文字インデックスの区間)を求める。矩形内に文字が無い場合は
  中心点までの距離が最も近い文字を採用し、ドラッグがわずかに文字の外側へ外れても選択が
  破綻しないようにしている。
- 選択確定時、`GroupLettersIntoLineRects`が行(隣接文字のBottom座標が2pt以上離れていれば
  改行とみなす)ごとに外接矩形を求め、`PendingSelection`(`SourcePageIndex`+行ごとの矩形群)へ
  反映する。複数行にまたがる選択は複数の矩形として保持される(PDFのLinkアノテーションの
  `/Rect`が単一矩形のみのため)。
- `CreateLinkToBookmark(bookmark)` — `PendingSelection`の各行矩形について、選択したしおりの
  `DestinationType`/座標をそのままコピーした`LinkAnnotationNode`を生成し`Links`へ追加する
  (複数行なら同一`GroupId`の複数リンクになる)。
- `PickArbitraryTargetAndCreateLink(targetPageIndex, pdfX, pdfY)` —
  `IsPickingArbitraryTarget`中にプレビュー上でクリックされた位置をXYZ形式のジャンプ先として、
  同様にリンクを確定する。
- `LinkGroups`(`ReactivePropertySlim<IReadOnlyList<LinkGroupInfo>>`) — `Links`を`GroupId`単位で
  集約した一覧UI向けの要約情報。`Links.CollectionChanged`のたびに再計算される。

### 7.5 `DeleteLinkGroup` / `BeginEditLinkGroup`

`DeleteLinkGroup(groupId)`は該当`GroupId`の全リンクを`Links`から削除する。
`BeginEditLinkGroup(groupId)`は該当リンクをいったん`Links`から削除し、同じホットスポット
(`SourceRect`群)を`PendingSelection`へ復元する — これにより`CreateLinkToBookmark`/
`PickArbitraryTargetAndCreateLink`をそのまま使って新しいジャンプ先を選び直せる(確定後は
新しい`GroupId`が振られる。`GroupId`自体は内部的な集約用の値でしかないため、編集の前後で
同一である必要はない)。いずれも、対象に既存リンク(後述)が1件でも含まれる場合は何もしない
([§7.6](#link-editor-existing-links)参照)。

<a id="link-editor-finish"></a>
### 7.6 `FinishAsync` — 完了(リンクの書き込み)と冪等性

<a id="link-editor-existing-links"></a>
`PdfLinkAnnotationService.ApplyLinksAsync`は`Modify`モードでの**追記のみ**を行い、既存の注釈を
安全に削除・置換する手段を持たない([02-core-design.md §2.11](02-core-design.md#link-editor-services)
参照)。この制約から、`LinkEditorViewModel`は2つの設計判断をしている。

1. **「素の状態」バックアップと冪等な完了**: `LoadAsync`直後(まだ本セッションでリンクを
   一切反映していない状態)のファイルを、一時フォルダへ複製して`_pristineBackupPath`に
   保持する。`FinishAsync`は毎回まずこのバックアップを`FilePath`へ復元してから
   `ApplyLinksAsync`を呼ぶため、「完了」を複数回実行しても(その都度`Links`の内容が
   変わっていても)注釈が重複することはない。
2. **既存リンクの除外**: `LoadAsync`で`ReadExistingLinksAsync`により読み取った既存リンクは、
   `_preExistingLinkIds`(`HashSet<Guid>`)へIdを記録した上で`Links`へ追加する(一覧に表示するため)。
   バックアップから復元した時点でこれらは既にファイルへ含まれているため、`FinishAsync`は
   `Links`から`_preExistingLinkIds`に含まれる分を除いた**新規作成リンクのみ**を
   `ApplyLinksAsync`へ渡す(渡してしまうと二重に書き込まれる)。同じ理由で
   `DeleteLinkGroup`/`BeginEditLinkGroup`(§7.5)は既存リンクに対して何もしない(削除・ジャンプ先変更を「なかったこと」にする手段がないため)。
   `LinkGroupInfo.IsPreExisting`(全リンクが`_preExistingLinkIds`に含まれるグループはtrue)を
   UI側が参照し、既存リンクの編集・削除ボタンを非表示にする([04-ui-design.md §6.4](04-ui-design.md#link-editor-existing-links-ui)参照)。

<a id="link-editor-thread"></a>
### 7.7 実装中に発生した2つのバグ

- **クロススレッドクラッシュ(`ConfigureAwait(false)`)**: 実装初期、`LoadAsync`等の非同期
  メソッドが`.ConfigureAwait(false)`を使っていたため、最初の`await`以降の継続処理が
  スレッドプールスレッドで実行され、その中で`IsBusy`等の`ReactivePropertySlim`を書き換えた
  瞬間にWPFの`CommandManager`(UIスレッド専用)が`InvalidOperationException`
  (クロススレッドアクセス)を投げていた。App層の他のViewModel(`BookmarkTreeViewModel`等)は
  そもそも`ConfigureAwait(false)`を使っておらず、この既定から外れていたことが原因。
  該当箇所を全て削除して解決した([01-architecture.md §4.5](01-architecture.md)参照)。
- **スクロール位置からの現在ページ検出の失敗**: 当初`VisualTreeHelper.HitTest`でビューポート
  上端の要素からページを特定していたが、WPF版が使う`Wpf.Ui`の`FluentWindow`は内部の
  `ScrollViewer`を独自の`PassiveScrollViewer`へ差し替えており、そのヒットテスト結果が
  内部コンテンツまで到達せず常に`PassiveScrollViewer`自身で止まっていた
  (診断ログを一時的に仕込んで特定)。全ページ同一のプレースホルダ高さを前提に、スクロール
  オフセットとページ高さから直接ページ番号を算出する方式へ変更して解決した(詳細は
  [04-ui-design.md §6.2](04-ui-design.md#link-editor-scroll-ui))。
