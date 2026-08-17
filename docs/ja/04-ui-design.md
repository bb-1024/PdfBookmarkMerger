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
| `EnumToVisibilityConverter`(WPF) / `EnumEqualsConverter`(Avalonia) | ○ | ○ | Enum値の一致判定(WPFは`Visibility`を返す、Avaloniaはboolを返し`IsVisible`にバインド) |
| `InverseBooleanConverter` | ○ | — | bool反転。WPFのみ存在(Avaloniaは`!Property`というバインディング構文を直接サポートするため不要) |

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
