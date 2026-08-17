# 04. UI Frontend Design (WPF / Avalonia)

Both the WPF build (`PdfBookmarkMerger.Wpf`) and the Avalonia build (`PdfBookmarkMerger.Avalonia`)
bind the `App` layer's ViewModels as-is, and each independently implements only these three kinds of
framework-specific work:

1. The `IDialogService` implementation (`WpfDialogService` / `AvaloniaDialogService`)
2. `MainWindow` code-behind (D&D, recomputing the title column width on Undo, busy-display timer
   control, preserving horizontal scroll position on a bookmark-row click, etc.)
3. `IValueConverter`s that drive XAML bindings

## 1. From app startup to the window showing

| | WPF build | Avalonia build |
|---|---|---|
| Entry point | `App` (an `Application` subclass) `OnStartup` | `Program.Main` → `App.OnFrameworkInitializationCompleted` |
| Unhandled-exception hooks | `AppDomain.UnhandledException` / `DispatcherUnhandledException` / `TaskScheduler.UnobservedTaskException` | `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` + wrapping all of `Program.Main` in `try/catch` (since Avalonia has no UI-thread-specific hook) |
| `IDialogService` implementation | `WpfDialogService` | `AvaloniaDialogService` |
| Theme application | `ThemeApplier.Apply(mainWindow, themeMode)` (uses `Wpf.Ui.Appearance.SystemThemeWatcher`) | `ThemeApplier.Apply(themeMode)` (Avalonia's `RequestedThemeVariant`) |

Both builds synchronously wait for `AppLanguageBootstrapper.ApplyAsync(userSettings)` to finish
before constructing `MainWindow` (see
[01-architecture.md §3](01-architecture.md#startup) for why).

## 2. What `MainWindow` code-behind is responsible for

The WPF build's `MainWindow.xaml.cs` and the Avalonia build's `MainWindow.axaml.cs` are structured
almost identically. Their main responsibilities:

<a id="col-width"></a>
### 2.1 Measuring and sharing the title column's width

Because each bookmark-tree row is shifted horizontally by the TreeView's hierarchy indent, the title
column would otherwise not line up vertically. `RecomputeTitleColumnWidth()` measures every node's
title with `FormattedText` and writes the maximum width into
`BookmarkTreeViewModel.TitleColumnBaseWidth`. Each row's actual width is then derived by
`DepthToTitleWidthConverter` (WPF) / the equivalent logic (Avalonia) as
`BaseWidth - Depth×IndentPerLevel`. **The indent constant differs between frameworks** (WPF: 19px,
Avalonia: 16px — matched to what each framework's own `TreeViewItem` actually applies). This
recompute is triggered after every operation that can change a title or the hierarchy —
`OnBookmarkTitleTextChanged`, after D&D, after level operations, after Undo, and so on.

### 2.2 Bookmark-tree D&D (reordering and re-parenting)

From detecting the start of a row drag (WPF: `PreviewMouseLeftButtonDown` + a movement-threshold
check in `PreviewMouseMove`; Avalonia: `PointerPressed` + `PointerMoved` subscribed on the Tunnel
phase, explained below), through deciding "insert as child" vs. "insert as sibling" based on drop
position (`ResolveBookmarkDropPlan`, checking whether the hit `TreeViewItem`'s header was struck in
its top or bottom half), drawing the insertion-point indicator line, to auto-scrolling while the
cursor sits near the top/bottom edge during a drag (`UpdateBookmarkAutoScroll`, ticked by a
`DispatcherTimer`) — all of it is implemented consistently end to end.

Clicking or dropping on a part of a row with no actual element underneath it (the margin to the left
of the level indicator, or to the right of the post-merge page display) finds nothing under the
default hit test. `SelectBookmarkRowAtY`/`ResolveBookmarkDropPlan` instead locate the row
geometrically from the cursor's Y coordinate via `FindTreeViewItemAtY`, so clicks and drops are
accepted across a row's full width.

The Avalonia build subscribes `PointerPressed` explicitly on the **Tunnel phase** because
`SelectingItemsControl`'s built-in selection handling already marks the event handled during the
Bubble phase, so a normal XAML event subscription (Bubble) never sees it in time to detect a drag
start (the same reason the WPF build uses `PreviewMouseLeftButtonDown`, a Tunnel-phase routed event).

<a id="scroll-fix"></a>
### 2.3 Preserving horizontal scroll position on a bookmark-row click (since v1.2.1)

Each row in the bookmark editor is wide — many controls laid out side by side — so a narrow window
shows a horizontal scrollbar. Clicking a row in that state used to trigger both WPF's and Avalonia's
default "bring the target fully into view on selection/focus change" behavior, which tried to reveal
the entire row (including its horizontal extent) and moved the horizontal scroll position
unexpectedly.

**An approach that was considered and rejected**: rewriting WPF's `RequestBringIntoView` routed event
to request only the vertical component. It was ruled out because the default "bring into view"
handling could execute earlier in the bubble path (on the `ScrollViewer` side) than a TreeView-level
handler, with no reliable way to guarantee ordering.

**The approach actually used**: capture the horizontal scroll position right after the click (before
selection processing starts) via `PreserveBookmarkTreeHorizontalScrollPosition`, then restore it once
the whole selection/focus-change pipeline has settled — at a low-priority dispatch point (WPF:
`Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ...)`; Avalonia:
`Dispatcher.UIThread.Post(..., DispatcherPriority.ContextIdle)`). This reliably preserves the
horizontal position regardless of which specific mechanism caused the change (the default
auto-scroll, or `Focus()` inside `SelectBookmarkRowAtY`). Vertical position is deliberately left
untouched, so keyboard navigation still auto-scrolls a selected off-screen row into view as before.
Clicks on the `ScrollBar` itself are excluded, so dragging the scrollbar isn't snapped back.

### 2.4 Busy-display detail-progress timer

Subscribes to `ViewModel.IsBusy`, and only shows the detail progress text (`BusyDetailText`) once
busy has been `true` for 5 seconds (a `DispatcherTimer` with that `Interval`) —
`OnIsBusyChanged`/`OnBusyDetailTimerTick`. This delay avoids a flicker of detail text on short-lived
operations.

### 2.5 File-list D&D and the placeholder hint

Handles D&D from outside the app (`OnWindowDrop`) as well as reordering within the file list
(`OnFileListDrop`); the WPF build additionally overlays a hint text via `AdornerLayer` while the file
list is empty (`UpdateFileListPlaceholder`, `PlaceholderTextAdorner` — this never touches the
ListBox's own visual tree, so it doesn't affect D&D hit testing).

<a id="expand-level-controls"></a>
### 2.6 Bulk expand/collapse-by-level controls (since v1.2.2)

Added a "-" button, a level-number text box, and a "+" button directly above the bookmark tree. The
text box is two-way bound to `BookmarkTreeViewModel.ExpandLevelInput` (string) (see
[03-app-design.md §2.7](03-app-design.md#expand-level) for the ViewModel-side logic). The WPF build
uses the text box's default `Text` binding (`UpdateSourceTrigger=LostFocus`) as-is; the Avalonia
build, like its other text boxes, applies the value as the user types. Both frontends subscribe to
the text box's `LostFocus` event in code-behind and call
`BookmarkTreeViewModel.NormalizeExpandLevelInput()` to normalize an invalid value back to empty.

## 3. Converters

| Converter | WPF | Avalonia | Role |
|---|:---:|:---:|---|
| `DepthToTitleWidthConverter` | ✓ | ✓ | Title column width from hierarchy depth + base width (the indent constant differs between WPF/Avalonia, see [§2.1](#col-width)) |
| `PageNumberWidthConverter` (since v1.2.0) | ✓ | ✓ | Text-box width for the pre-merge page number, from its digit count |
| `EditedHighlightBrushConverter` (since v1.2.0) | ✓ | ✓ | Highlight background (semi-transparent) for a row with an edited pre-merge page number |
| `ZoomPercentToStringConverter` | ✓ | ✓ | PDF Zoom ratio (1.0 = 100%) ⇔ percent string shown in the UI |
| `NullableDoubleToStringConverter` | ✓ | ✓ | A coordinate (`double?`) ⇔ text-box string |
| `BusyProgressToTextConverter` | ✓ | ✓ | `BusyProgressInfo` ⇒ a display string like "12 / 340 (processing: a.pdf, b.pdf)" |
| `PageCountToTextConverter` | ✓ | ✓ | Page count (`int?`) ⇒ a display string like "12 pages" |
| `ThemeModeToLabelConverter` | ✓ | ✓ | `ThemeMode` ⇒ localized display label |
| `AppLanguageToLabelConverter` | ✓ | ✓ | `AppLanguage` ⇒ localized display label |
| `EnumToVisibilityConverter` (WPF) / `EnumEqualsConverter` (Avalonia) | ✓ | ✓ | Enum equality check (WPF returns `Visibility`; Avalonia returns bool, bound to `IsVisible`) |
| `InverseBooleanConverter` | ✓ | — | bool negation. WPF-only (Avalonia supports the `!Property` binding syntax directly, so it doesn't need one) |

All of them are golden-tested against real instances of both the WPF and Avalonia implementations in
`PdfBookmarkMerger.UiConverters.Tests`' `ConverterParityTests` — note that the intent is deliberately
**not** always "both implementations compute the same result." For a converter like
`DepthToTitleWidthConverter`, where the indent constant itself legitimately differs between
frameworks, the test instead checks that "each implementation computes correctly per its own,
correct constant."

## 4. Dialog windows

`PropertiesDialogWindow`, `SettingsDialogWindow`, and `LevelCapDialogWindow` (WPF: `.xaml`/`.xaml.cs`;
Avalonia: `.axaml`/`.axaml.cs`) are thin wrappers that take the corresponding `*ViewModel` (App layer,
UI-independent) and set `DataContext`/`Owner`. Error/info messages have no dedicated window of their
own: the WPF build uses `Wpf.Ui.Controls.MessageBox`, and the Avalonia build constructs its own
`AlertWindow`, freshly each time.

## 5. Rebuilding the window on a language switch

Changing the display language in the Settings dialog first switches `Strings.Culture` via
`AppLanguageBootstrapper.ApplyImmediate`, then `WpfDialogService.ReplaceMainWindowForLanguageChange` /
`AvaloniaDialogService.ReplaceMainWindowForLanguageChange` constructs a new `MainWindow` that carries
forward **the same `MainWindowViewModel` instance** (preserving loaded files, the tree being edited,
and so on), copies position/size/window state across, and closes the old window. The old window's
`Closed` event disposes `_viewModelSubscriptions` (a `CompositeDisposable`), so its callbacks don't
keep firing against a now-closed window's UI elements on every subsequent ViewModel change. The WPF
build additionally has to call `Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(oldWindow)` explicitly —
without it, the closed window stays referenced by the watch list for the rest of the process's
lifetime.
