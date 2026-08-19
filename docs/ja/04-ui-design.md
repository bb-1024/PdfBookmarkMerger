# 04. UIフロントエンド設計(WPF / Avalonia)

WPF版(`PdfBookmarkMerger.Wpf`)・Avalonia版(`PdfBookmarkMerger.Avalonia`)は、いずれも
`App` 層のViewModelをそのままバインドし、以下の3種のフレームワーク固有処理のみを
それぞれ独立に実装する。

1. `IDialogService` の実装(`WpfDialogService` / `AvaloniaDialogService`)
2. `MainWindow` のコードビハインド(D&D、Undo検知に伴うタイトル列幅再計算、
   busy表示のタイマー制御、しおり行クリック時の横スクロール位置維持 等)
2. `IValueConverter`(WPF)/ `IValueConverter`(Avalonia)によるXAMLバインディング変換

## 1. アプリ起動〜ウィンドウ表示

| | WPF版 | Avalonia版 |
|---|---|---|
| エントリポイント | `App`(`Application` 派生)の `OnStartup` | `Program.Main` → `App.OnFrameworkInitializationCompleted` |
| 未処理例外フック | `AppDomain.UnhandledException` / `DispatcherUnhandledException` / `TaskScheduler.UnobservedTaskException` | `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` + `Program.Main` 全体を`try/catch`(UIスレッド専用フックがAvaloniaに存在しないための代替) |
| `IDialogService` 実装 | `WpfDialogService` | `AvaloniaDialogService` |
| テーマ適用 | `ThemeApplier.Apply(mainWindow, themeMode)`(`Wpf.Ui.Appearance.SystemThemeWatcher` 使用) | `ThemeApplier.Apply(themeMode)`(Avaloniaの `RequestedThemeVariant`) |

両者とも `MainWindow` を構築する前に `AppLanguageBootstrapper.ApplyAsync(userSettings)` を
同期的に待ち合わせて完了させる(理由は [01-architecture.md §3](01-architecture.md#startup) 参照)。

## 2. `MainWindow` コードビハインドの責務

WPF版(`MainWindow.xaml.cs`)・Avalonia版(`MainWindow.axaml.cs`)は、構造がほぼ並行しており
主な責務は以下の通り。

<a id="col-width"></a>
### 2.1 タイトル列幅の実測・共有

しおりツリーの各行はTreeView階層インデントの分だけタイトル列が縦にずれるため、
`RecomputeTitleColumnWidth()` が全ノードのタイトルを `FormattedText` で実測し、
最大幅を `BookmarkTreeViewModel.TitleColumnBaseWidth` へ書き込む。各行の実際の幅は
`DepthToTitleWidthConverter`(WPF) / 同等ロジック(Avalonia)が `BaseWidth - Depth×IndentPerLevel`
として求める。**インデント定数はフレームワークごとに異なる**(WPF: 19px、Avalonia: 16px、
それぞれのTreeViewItemが実際に適用するインデント幅に合わせてある)。この定数は
`OnBookmarkTitleTextChanged`・D&D後・レベル操作後・Undo後など、タイトルや階層が変わりうる
すべての操作の後に再計算を呼ぶ形で反映される。

### 2.2 しおりツリーのD&D(並べ替え・再親子付け)

行のドラッグ開始検知(`PreviewMouseLeftButtonDown`+`PreviewMouseMove` の移動量しきい値判定
(WPF)/ `PointerPressed`+`PointerMoved` をTunnelフェーズで購読(Avalonia、理由は後述))から、
ドロップ位置に応じた「子として挿入」/「兄弟として挿入」の判定(`ResolveBookmarkDropPlan`、
ヒットしたTreeViewItemのヘッダー領域の上半分/下半分で判定)、挿入位置インジケータ線の描画、
ドラッグ中にカーソルが上端/下端付近にある間の自動スクロール(`UpdateBookmarkAutoScroll`、
`DispatcherTimer` で一定間隔スクロール)までを一貫して実装する。

行の実要素(タイトル欄・ComboBox等)が存在しない部分(レベル表示の左側の余白、結合後ページ
表示の右側の余白)をクリック・ドロップした場合、既定のヒットテストでは対象が見つからない。
`SelectBookmarkRowAtY`/`ResolveBookmarkDropPlan` は、カーソルのY座標から `FindTreeViewItemAtY`
で幾何的に行を探すことで、行の全幅でクリック・ドロップを受け付けるようにしている。

Avalonia版が `PointerPressed` を **Tunnelフェーズ** で明示的に購読しているのは、
`SelectingItemsControl` の既定の選択処理がBubbleフェーズで先にイベントをハンドル済みにしてしまい、
通常のXAMLイベント購読(Bubble)ではD&D開始検知用のハンドラに一切イベントが届かないため
(WPF版が `PreviewMouseLeftButtonDown` というTunnelフェーズの専用ルーティングイベントを
使っているのと同じ理由)。

<a id="scroll-fix"></a>
### 2.3 しおり行クリック時の横スクロール位置維持(v1.2.1〜)

しおり編集ツリーの1行は多数のコントロールを横に並べた幅広の内容のため、ウィンドウが狭い場合は
横スクロールバーが表示される。この状態で行をクリックすると、WPF・Avalonia双方の既定動作
(選択・フォーカス変更に伴う「対象を画面内へ収める」処理)が行全体(横方向を含む)を
表示しようとし、意図せず横スクロール位置が動いてしまう不具合があった。

**検討したが採用しなかった案**: WPFの `RequestBringIntoView` ルーテッドイベントを横方向のみ
差し替えて再要求する方式。既定の「対象を画面内へ収める」処理がイベントのバブル経路上で
TreeView側のハンドラより先(ScrollViewer側)に実行される可能性を排除できず、確実性に欠けると
判断して不採用とした。

**採用した方式**: クリック直後(選択処理が始まる前)の横スクロール位置を保存し
(`PreserveBookmarkTreeHorizontalScrollPosition`)、選択・フォーカス変更に伴う一連の処理が
完了した後のタイミング(WPF: `Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ...)`、
Avalonia: `Dispatcher.UIThread.Post(..., DispatcherPriority.ContextIdle)`)に元の位置へ復元する。
原因となる個々の処理(既定の自動スクロールか、`SelectBookmarkRowAtY` 内の `Focus()` か等)を
問わず確実に横スクロール位置を保てる。縦方向は復元しないため、キーボード操作で画面外の行を
選択した場合の縦方向自動スクロールは従来どおり機能する。横スクロールバー自体の操作
(`ScrollBar` 上でのクリック・ドラッグ)は対象外とし、巻き戻してしまわないようにしている。

### 2.4 busy表示の詳細進捗タイマー

`ViewModel.IsBusy` の変化を購読し、busyがtrueになってから5秒(`DispatcherTimer` の
`Interval`)経過して初めて詳細進捗テキスト(`BusyDetailText`)を表示する
(`OnIsBusyChanged`/`OnBusyDetailTimerTick`)。短時間で終わる処理での表示のちらつきを防ぐための
遅延表示。

### 2.5 ファイル一覧のD&D・プレースホルダー表示

エクスプローラー等外部からのD&D(`OnWindowDrop`)、ファイル一覧内でのD&D並べ替え
(`OnFileListDrop`)に加え、WPF版はファイル一覧が空の間 `AdornerLayer` 経由でヒントテキストを
重ねる(`UpdateFileListPlaceholder`、`PlaceholderTextAdorner`。ListBoxの可視ツリー自体は
変更しないためD&Dのヒットテストに影響しない)。

<a id="expand-level-controls"></a>
### 2.6 ツリー開閉レベルの一括指定コントロール(v1.2.2〜)

しおり編集ツリー直上に「-」ボタン・レベル指定テキストボックス・「+」ボタンを追加している。
テキストボックスは `BookmarkTreeViewModel.ExpandLevelInput`(string)へ双方向バインドする
(ViewModel側のロジックは
[03-app-design.md §2.7](03-app-design.md#expand-level) を参照)。WPF版はテキストボックスの
既定の `Text` バインディング(`UpdateSourceTrigger=LostFocus`)をそのまま使い、Avalonia版は
他のテキストボックス同様、入力のたびに反映される。両フロントエンドとも、テキストボックスの
`LostFocus` イベントをコードビハインドで購読し、`BookmarkTreeViewModel.NormalizeExpandLevelInput()`
を呼んで不正な入力値を空欄へ正規化する。

## 3. Converter一覧

| Converter | WPF | Avalonia | 役割 |
|---|:---:|:---:|---|
| `DepthToTitleWidthConverter` | ○ | ○ | 階層深さ+基準幅からタイトル列幅を算出(インデント定数がWPF/Avaloniaで異なる、[§2.1](#col-width)参照) |
| `PageNumberWidthConverter`(v1.2.0〜) | ○ | ○ | 結合前ページ数の桁数からテキストボックス幅を算出 |
| `EditedHighlightBrushConverter`(v1.2.0〜) | ○ | ○ | 結合前ページ数編集済み行の強調背景色(半透明) |
| `ZoomPercentToStringConverter` | ○ | ○ | PDFのZoom倍率(1.0=100%)⇔UI表示用パーセント文字列 |
| `NullableDoubleToStringConverter` | ○ | ○ | 座標(`double?`)⇔テキストボックス文字列 |
| `BusyProgressToTextConverter` | ○ | ○ | `BusyProgressInfo` ⇒「12 / 340件 (処理中: a.pdf, b.pdf)」等の表示文字列 |
| `PageCountToTextConverter` | ○ | ○ | ページ数(`int?`)⇒「12ページ」等の表示文字列 |
| `ThemeModeToLabelConverter` | ○ | ○ | `ThemeMode` ⇒ 表示ラベル(i18n対応) |
| `AppLanguageToLabelConverter` | ○ | ○ | `AppLanguage` ⇒ 表示ラベル(i18n対応) |
| `EnumToVisibilityConverter`(WPF) / `EnumEqualsConverter`(Avalonia) | ○ | ○ | 値とConverterParameterの一致判定(文字列比較、boolにも使える。WPFは`Visibility`を返す、Avaloniaはboolを返し`IsVisible`にバインド) |
| `InverseBooleanConverter` | ○ | — | bool反転。WPFのみ存在(Avaloniaは`!Property`というバインディング構文を直接サポートするため不要) |
| `ByteArrayToImageConverter` | ○ | ○ | `byte[]?`(PNG)⇒ `BitmapImage`(WPF)/`Bitmap`(Avalonia)。リンク編集画面のページプレビュー用 |
| `LinkGroupDisplayConverter` | ○ | ○ | `LinkGroupInfo` ⇒「{ソースページ}ページ目 → {ジャンプ先ページ}ページ目」(1始まり表示) |
| `NotNullToVisibilityConverter`(WPF) | ○ | — | 値がnullでなければ表示。Avaloniaは`ObjectConverters.IsNotNull`(Avalonia標準)を直接使うため専用実装は無い |

すべて `PdfBookmarkMerger.UiConverters.Tests` の `ConverterParityTests` で、WPF/Avalonia
双方の実装を直接インスタンス化してゴールデンテストする(意図的に「両実装の計算結果が一致する」
ことを検証するのではなく、`DepthToTitleWidthConverter` のようにインデント定数自体が異なる
コンバータについては「各実装が自身の正しい定数通りに計算しているか」を検証する形にしている
点に注意)。

## 4. ダイアログウィンドウ

`PropertiesDialogWindow`・`SettingsDialogWindow`・`LevelCapDialogWindow`(WPF: `.xaml`/`.xaml.cs`、
Avalonia: `.axaml`/`.axaml.cs`)は、対応する `*ViewModel`(App層、UI非依存)を受け取って
`DataContext`/`Owner` を設定するだけの薄いラッパー。エラー/情報表示は専用ウィンドウを持たず、
WPF版は `Wpf.Ui.Controls.MessageBox`、Avalonia版は自前の `AlertWindow` を都度生成する。

## 5. 言語切り替え時のウィンドウ再構築

設定ダイアログで表示言語を変更すると、`AppLanguageBootstrapper.ApplyImmediate` で
`Strings.Culture` を切り替えた直後、`WpfDialogService.ReplaceMainWindowForLanguageChange` /
`AvaloniaDialogService.ReplaceMainWindowForLanguageChange` が、**同じ `MainWindowViewModel`
インスタンス**(読み込み済みファイル・編集中のしおりツリー等の状態を保持)を引き継いだ新しい
`MainWindow` を構築し、位置・サイズ・ウィンドウ状態をコピーした上で旧ウィンドウを閉じる。
旧ウィンドウの `Closed` イベントで `_viewModelSubscriptions`(`CompositeDisposable`)を破棄し、
古いウィンドウのコールバックがViewModelの変化のたびに実行され続けることを防ぐ。WPF版は
さらに `Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(oldWindow)` を明示的に呼ぶ必要がある
(監視対象から外れないと、閉じたはずの旧ウィンドウがプロセス生存中ずっと参照され続ける)。

<a id="link-editor-ui"></a>
## 6. リンク編集画面(手順5、v1.3.0〜)

`LinkEditorViewModel`(App層、[03-app-design.md §7](03-app-design.md#link-editor)参照)を
バインドする画面。ページ送りツールバー([前のページ][ページ番号入力欄][/ 総ページ数][次のページ]
[縮小][拡大])・連続スクロールのプレビュー・しおり一覧(ジャンプ用、`TreeView`で全階層を
常に展開表示)・設定済みリンク一覧を持つ。

<a id="link-editor-scroll-ui"></a>
### 6.1 連続スクロールプレビューの実体化

プレビューは、仮想化された `ListBox`(WPF: `VirtualizingPanel.IsVirtualizing="True"` +
`VirtualizationMode="Recycling"` + `ScrollUnit="Pixel"`、Avalonia: 既定で仮想化された
`VirtualizingStackPanel`)を `LinkEditorViewModel.PageSlots` にバインドしたもの。各アイテムの
`DataTemplate`ルート要素へ`Loaded`/`Unloaded`イベントハンドラ(`OnPageSlotLoaded`/
`OnPageSlotUnloaded`)を付け、コンテナが実体化・リサイクルされるたびに
`LinkEditorViewModel.LoadPageSlotAsync`/`UnloadPageSlot`を呼ぶ(詳細は
[03-app-design.md §7.2](03-app-design.md#link-editor-scroll)参照)。選択・リンク作成の
ヒットレイヤー(透明な`Rectangle`)とホットスポットのオーバーレイ(`Canvas`)は、各アイテムの
`DataTemplate`内に常に存在するが、`IsCurrent`がtrueのアイテムでのみ`Visibility`/`IsVisible`が
trueになる(=現在ページ以外は非表示・非ヒットテスト)。

<a id="link-editor-scroll-fix"></a>
### 6.2 スクロール連動のページ検出とページ送りの位置合わせ

`ScrollViewer.ScrollChanged`(WPF: `ListBox`要素へ添付イベントとして購読、Avalonia:
同様)のたびに、「ビューポート内で最も表示面積が大きいページ」を`CurrentPageIndex`へ
反映する(`OnPdfPreviewScrollChanged`)。全ページが`PlaceholderWidth`/`PlaceholderHeight`
(先頭ページのサイズを流用)で統一されている前提を使い、候補ページの範囲を
`(int)(viewportTop / itemHeight)`前後に絞った上で、各候補の可視高さ
(`Math.Min(viewportBottom, itemTop+itemHeight) - Math.Max(viewportTop, itemTop)`)を
比較して最大のものを採用する。

この方式に落ち着くまでに2つの実装を試して破棄している。

1. **`VisualTreeHelper.HitTest`/`InputHitTest`でビューポート上端をヒットテストする方式**
   (WPF/Avalonia双方で試した) — Avalonia版は問題なく動いたが、WPF版はこのアプリが使う
   `Wpf.Ui`の`FluentWindow`が内部の`ScrollViewer`を独自の`PassiveScrollViewer`へ自動的に
   差し替えており、そのヒットテスト結果が常に`PassiveScrollViewer`自身で止まり内部の
   ページコンテンツまで到達しなかった(一時的な診断ログで`hit`の実際の型を出力して特定)。
2. **ビューポート上端のページだけをCurrentPageIndexとする方式**(面積比較を使わない単純版) —
   機能はしたが、ユーザーからのフィードバックで「プレビュー領域を占める割合が一番大きい
   ページを指すようにしてほしい」との要望があり、面積比較方式へ変更した。

ページ送りボタン・しおりジャンプ・ページ番号入力によるページ移動(`ScrollToPage`)は、
`ListBox.ScrollIntoView`を**使わない**。`ScrollIntoView`は「対象が見えるようになる最小限の
スクロール」しか行わないため、下方向へ移動する場合は対象ページの**末尾**がビューポート下端に
揃ってしまい先頭に揃わない(WPFの既知の挙動)。代わりに、対象ページの先頭が正確にビューポート
上端へ来るオフセット(`pageIndex * (PlaceholderHeight + itemMargin)`)を直接計算して
`ScrollViewer.ScrollToVerticalOffset`(WPF)/`ScrollViewer.Offset`(Avalonia)へ設定する。

ページ送りボタン等プログラム的な移動によるスクロールと、ユーザーの手動スクロールに追従して
`CurrentPageIndex`を更新する経路が同じ`ScrollChanged`イベントを共有するため、
`_isSyncingCurrentPageFromScroll`(bool)で「今まさにスクロール操作からCurrentPageIndexを
追従させている最中」を示し、この間は`CurrentPageIndex`の変更を受けてもスクロール位置を
動かし直さない(動かすと、追従した瞬間に別の位置へジャンプし直してスクロールが成立しなくなる)。

### 6.3 リンクのホットスポット表示・テキスト選択

`RedrawLinkOverlay`は、`PdfPageListBox.ItemContainerGenerator.ContainerFromIndex`(WPF)/
`ContainerFromIndex`(Avalonia)で現在ページのコンテナを取得し(仮想化により未実体化なら
何もしない — コンテナが実体化した時に`OnPageSlotLoaded`から改めて呼ばれる)、その中の
`Canvas`(`LinkOverlayCanvas`)へ確定済みリンクを半透明の矩形として描画し直す。マウス/
ポインタイベント(`OnPdfPreviewMouseLeftButtonDown`等)はヒットレイヤー自身(`sender`)を
基準に座標を取り、兄弟要素の`Canvas`を`VisualTreeHelper`(WPF)/`GetVisualDescendants`
(Avalonia)で探して描画対象にする — 連続スクロール表示では同名の`Canvas`が複数の
コンテナに存在しうるため、常に「今操作しているコンテナ自身」のものを使う必要がある。

<a id="link-editor-existing-links-ui"></a>
### 6.4 設定済みリンク一覧と既存リンクの扱い

リンク一覧(`LinkEditor.LinkGroups.Value`)の各項目は「表示」(ジャンプして確認)・「編集」・
「削除」の3ボタンを持つが、`LinkGroupInfo.IsPreExisting`(ファイルに元から含まれていたリンク、
[03-app-design.md §7.6](03-app-design.md#link-editor-existing-links)参照)がtrueの項目は
「編集」「削除」ボタンを非表示にし、代わりに「(既存)」バッジを表示する(`PdfLinkAnnotationService`
が既存の注釈を安全に削除・置換できないため、この画面からは操作できない)。

### 6.5 設定ダイアログ: リンク編集ボタンの表示切り替え

設定ダイアログに「PDF結合時にリンク編集ボタンを表示する」チェックボックス
(`SettingsViewModel.ShowMergeAndEditLinksButton`)を追加している。既定はオフ
(設定ファイル未読み込み時を含む)。ダイアログでOKを押した時点で
`MainWindowViewModel.ShowMergeAndEditLinksButton`へ反映され、しおり編集画面の
「結合してリンク編集へ進む」ボタンの`Visibility`/`IsVisible`バインディングが即座に追従する
(`ThemeMode`/`Language`と異なり、ウィンドウの再構築は不要)。
