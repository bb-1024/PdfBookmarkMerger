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
| `EnumToVisibilityConverter` (WPF) / `EnumEqualsConverter` (Avalonia) | ✓ | ✓ | Equality check between a value and ConverterParameter (string comparison; also works for bool. WPF returns `Visibility`; Avalonia returns bool, bound to `IsVisible`) |
| `InverseBooleanConverter` | ✓ | — | bool negation. WPF-only (Avalonia supports the `!Property` binding syntax directly, so it doesn't need one) |
| `ByteArrayToImageConverter` | ✓ | ✓ | `byte[]?` (PNG) ⇒ `BitmapImage` (WPF) / `Bitmap` (Avalonia). Used for the link editor's page preview |
| `LinkGroupDisplayConverter` | ✓ | ✓ | `LinkGroupInfo` ⇒ "Page {source} → Page {target}" (1-based display) |
| `NotNullToVisibilityConverter` (WPF) | ✓ | — | Visible when the value isn't null. Avalonia uses its own built-in `ObjectConverters.IsNotNull` directly instead of a dedicated implementation |

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

<a id="link-editor-ui"></a>
## 6. The link editor screen (step 5, since v1.3.0)

The screen that binds `LinkEditorViewModel` (App layer, see
[03-app-design.md §7](03-app-design.md#link-editor)). It has a page-turn toolbar
([Previous][editable page-number box][/ total pages][Next][Zoom out][Zoom in]), a continuous-scroll
preview, a bookmark list (for jumping — a `TreeView` that always shows every level expanded), and a
list of configured links.

<a id="link-editor-scroll-ui"></a>
### 6.1 Realizing the continuous-scroll preview

The preview is a virtualized `ListBox` (WPF: `VirtualizingPanel.IsVirtualizing="True"` +
`VirtualizationMode="Recycling"` + `ScrollUnit="Pixel"`; Avalonia: a `VirtualizingStackPanel`,
virtualized by default) bound to `LinkEditorViewModel.PageSlots`. Each item template's root element
has `Loaded`/`Unloaded` handlers (`OnPageSlotLoaded`/`OnPageSlotUnloaded`) that call
`LinkEditorViewModel.LoadPageSlotAsync`/`UnloadPageSlot` whenever a container is realized or recycled
(see [03-app-design.md §7.2](03-app-design.md#link-editor-scroll)). The selection/link-creation hit
layer (a transparent `Rectangle`) and the hotspot overlay (a `Canvas`) both exist permanently inside
every item's template, but only become visible/hit-testable on the item whose `IsCurrent` is true — so
every page except the current one stays invisible to input and undrawn.

<a id="link-editor-scroll-fix"></a>
### 6.2 Scroll-driven page detection and page-turn alignment

On every `ScrollViewer.ScrollChanged` (WPF: subscribed as an attached event on the `ListBox`; Avalonia:
the same), `OnPdfPreviewScrollChanged` writes "whichever page occupies the largest area of the
viewport" into `CurrentPageIndex`. Since every page shares one `PlaceholderWidth`/`PlaceholderHeight`
(page 0's size stands in for all), the candidate range is narrowed to roughly
`(int)(viewportTop / itemHeight)` and its neighbors, then each candidate's visible height
(`Math.Min(viewportBottom, itemTop+itemHeight) - Math.Max(viewportTop, itemTop)`) is compared and the
largest wins.

Two other implementations were tried and discarded before landing on this one:

1. **Hit-testing the element at the viewport's top edge** (`VisualTreeHelper.HitTest`/
   `InputHitTest`, tried on both WPF and Avalonia) — worked fine on Avalonia, but on WPF this app's
   `Wpf.Ui`-provided `FluentWindow` silently swaps the internal `ScrollViewer` for its own
   `PassiveScrollViewer`, whose hit-test result always resolved to the control itself rather than its
   actual page content (found by temporarily logging the hit's real type).
2. **Treating whichever page sits at the viewport's top edge as current** (a simpler version without
   the area comparison) — worked, but user feedback specifically asked for "whichever page occupies
   the largest share of the preview area," so it was replaced with the area-comparison version above.

Page-turn navigation (buttons, bookmark jumps, and the page-number text box — `ScrollToPage`)
deliberately does **not** use `ListBox.ScrollIntoView`. `ScrollIntoView` only scrolls the minimum
distance needed to bring the target into view, so scrolling downward to a page could leave its
**tail**, not its head, aligned to the viewport's bottom edge (a known WPF behavior). Instead,
`ScrollToPage` computes the exact offset that puts the target page's top precisely at the viewport's
top edge (`pageIndex * (PlaceholderHeight + itemMargin)`) and sets
`ScrollViewer.ScrollToVerticalOffset` (WPF) / `ScrollViewer.Offset` (Avalonia) directly.

Because both programmatic page-turn scrolling and the user's own manual scrolling drive
`CurrentPageIndex` through the same `ScrollChanged` event, `_isSyncingCurrentPageFromScroll` (bool)
marks "currently following the user's manual scroll into `CurrentPageIndex`" — while it's set, a
`CurrentPageIndex` change doesn't re-trigger a scroll (doing so would jump the view to yet another
position the instant it caught up, and scrolling would never settle).

### 6.3 Link hotspot display and text selection

`RedrawLinkOverlay` locates the current page's realized container via
`PdfPageListBox.ItemContainerGenerator.ContainerFromIndex` (WPF) / `ContainerFromIndex` (Avalonia) —
a no-op if it isn't realized yet (virtualization; it gets called again from `OnPageSlotLoaded` once it
is), then redraws the confirmed links as translucent rectangles into that container's `Canvas`
(`LinkOverlayCanvas`). Mouse/pointer handlers (`OnPdfPreviewMouseLeftButtonDown` etc.) take
coordinates relative to the hit layer itself (`sender`), then locate the sibling `Canvas` via
`VisualTreeHelper` (WPF) / `GetVisualDescendants` (Avalonia) to draw into — since the continuous-scroll
view can have a same-named `Canvas` inside multiple containers at once, the code must always resolve
the one belonging to whichever container is actually being interacted with.

<a id="link-editor-existing-links-ui"></a>
### 6.4 The link list and how pre-existing links are handled

Each entry in the link list (`LinkEditor.LinkGroups.Value`) normally has three buttons — "Jump" (scroll
to and verify it), "Edit," and "Delete." Whenever `LinkGroupInfo.IsPreExisting` is true (the link was
already present in the file — see
[03-app-design.md §7.6](03-app-design.md#link-editor-existing-links)), the Edit and Delete buttons are
hidden and a "(existing)" badge is shown instead, since `PdfLinkAnnotationService` has no way to safely
delete or replace an existing annotation, so this screen can't act on them.

### 6.5 Settings dialog: toggling the link-editing button

The Settings dialog has a "Show the link-editing button when merging PDFs" checkbox
(`SettingsViewModel.ShowMergeAndEditLinksButton`). It defaults to off, including when no settings file
has been loaded yet. Confirming the dialog with OK writes the value straight to
`MainWindowViewModel.ShowMergeAndEditLinksButton`, and the bookmark editor's "Merge and Continue to
Link Editing" button's `Visibility`/`IsVisible` binding picks it up immediately (unlike
`ThemeMode`/`Language`, no window rebuild is involved).
