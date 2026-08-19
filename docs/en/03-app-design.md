# 03. App Layer Design

`PdfBookmarkMerger.App` is the UI-framework-independent ViewModel and shared-app-service layer.
`ServiceCollectionExtensions.AddPdfBookmarkMergerApp()` registers `IUserSettingsService` /
`FileListViewModel` / `BookmarkTreeViewModel` / `MainWindowViewModel` as Singletons.

Every ViewModel derives from `ViewModelBase` (a thin base class that only owns a
`CompositeDisposable Disposables`), and exposes properties/commands through
[Reactive.Bindings](https://github.com/runceel/ReactiveProperty)'s `ReactivePropertySlim<T>` /
`ReactiveCommand` / `AsyncReactiveCommand`.

## 1. `MainWindowViewModel`

Orchestrates the main window as a whole: the `Step` transition
(`WorkflowStep.SelectFiles` → `EditBookmarks` → optionally `EditLinks`) and the primary commands.

| Command | CanExecute | What it does |
|---|---|---|
| `ConfirmFilesCommand` | `HasFiles && !IsBusy` | Reads every file's metadata in parallel → extracts bookmarks and computes post-merge page numbers → `BookmarkTree.Load` → `Step = EditBookmarks` |
| `MergeCommand` | `(Step==EditBookmarks) && !IsBusy && !HasPageNumberEdits` | `MergeCoreAsync(continueToLinkEditing: false)` — merges and ends the workflow here |
| `MergeAndEditLinksCommand` | Same | `MergeCoreAsync(continueToLinkEditing: true)` — merges, then `LinkEditor.LoadAsync` → `Step = EditLinks` |
| `SaveBookmarkSettingsCommand` | `(Step==EditBookmarks) && !IsBusy && !HasPageNumberInconsistency` | Save dialog → `BookmarkSettingsExportService.ExportAsync` |
| `BackToFileListCommand` | `(Step==EditBookmarks) && !IsBusy` | Returns to `Step = SelectFiles` |
| `BackToBookmarksCommand` | `(Step==EditLinks) && !IsBusy` | Returns to `Step = EditBookmarks` (the merged intermediate file is left as-is) |
| `FinishLinkEditingCommand` | `(Step==EditLinks) && !IsBusy` | `LinkEditor.FinishAsync()` → completion dialog |

`MergeCommand` and `MergeAndEditLinksCommand` are two entry points into a shared
`MergeCoreAsync(bool continueToLinkEditing)` that owns the actual merge (save-path dialog → properties
dialog if configured → `PdfMergeService.MergeAsync`). `MergeAndEditLinksCommand` (the "Merge and
Continue to Link Editing" button) only appears in the UI when the `ShowMergeAndEditLinksButton`
setting is on (default off, including when no settings file has been loaded yet;
`MainWindowViewModel.ShowMergeAndEditLinksButton` reflects it immediately once the Settings dialog is
confirmed).

`ConfirmFilesAsync` parallelizes each file's metadata read with a `SemaphoreSlim`
(capped at `Math.Clamp(Environment.ProcessorCount, 1, 8)`) and applies results in completion order
via `Task.WhenEach`. A file that fails to load gets flagged on
`PdfFileEntryViewModel.LoadFailed` and is thereafter excluded consistently from **both** the
bookmark tree construction and the actual merge (`MergeAsync`) — there's a regression test guarding
against an earlier bug where a failed file stayed excluded from the tree but still slipped into the
merge target list.

`MergeCommand` requires `!HasPageNumberEdits` because, while a pre-merge page-number edit is
pending, the merged PDF's actual page positions no longer match what's shown on screen / exported.
`SaveBookmarkSettingsCommand` requires `!HasPageNumberInconsistency` because an edit that drives a
page number below 1 can't be exported as valid XML (`HasPageNumberEdits`/`HasPageNumberInconsistency`
are owned by `BookmarkTreeViewModel`, covered below).

### 1.1 Forwarding busy/progress (since v1.2.1)

```csharp
BookmarkTree.IsBusy.Subscribe(busy => { ...; IsBusy.Value = busy; });
BookmarkTree.BusyProgress.Subscribe(p => BusyProgress.Value = p);
```

Subscribing to these two in the constructor makes the busy state produced by
`BookmarkTreeViewModel`'s internal large-node recompute (see [§2.6](#recompute) below) flow straight
through the same `IsBusy`/`BusyProgress` pipeline already used by file loading and merging, so it
reaches the UI (the busy overlay) unmodified. When busy starts, the current `StatusMessage` is
stashed and swapped for "Updating bookmark information…"; it's restored when busy ends.

## 2. `BookmarkTreeViewModel`

The central ViewModel behind the bookmark editor (steps 2/3). Keeps reordering/re-parenting via D&D,
add/remove, level operations, and property edits like the title in sync with the
`Core.Models.BookmarkNode` tree (`_rootModel`).

### 2.1 Public properties

| Property | Type | Role |
|---|---|---|
| `RootNodes` | `ObservableCollection<BookmarkNodeViewModel>` | The root-level nodes (what the UI binds to) |
| `ForceFitForAll` | `ReactivePropertySlim<bool>` | While on, disables every node's destination-type/coordinate controls and treats every node as `Fit` when merging (does not touch each node's actual stored setting) |
| `GlobalExpandOverride` | `ReactivePropertySlim<bool?>` | The 3-state "set expand state for all" toggle (`true` = expand all / `false` = collapse all / `null` = follow each node's own setting) |
| `CanUndo` / `UndoCommand` | — | Covered in [§2.4](#undo) below |
| `HasPageNumberEdits` / `HasPageNumberInconsistency` | `ReactivePropertySlim<bool>` | Pre-merge page-number-edit state (feeds into `MainWindowViewModel`'s CanExecutes) |
| `IsBusy` / `BusyProgress` | `ReactivePropertySlim<bool>` / `ReactivePropertySlim<BusyProgressInfo?>` | Covered in [§2.6](#recompute) below |
| `TitleColumnBaseWidth` | `ReactivePropertySlim<double>` | The title column's shared base width; measured on the UI side (`MainWindow.xaml.cs` etc.) |
| `ExpandLevelInput` / `CollapseAllCommand` / `ExpandAllCommand` | — | Covered in [§2.7](#expand-level) below |

### 2.2 Structural edits (add, remove, move, level operations)

`AddRoot` / `AddChild` / `AddSiblingAfter` / `Remove` / `Move` all follow the same pattern: push an
Undo snapshot → update both `RootNodes` (the UI-facing `ObservableCollection`) and `_rootModel` (the
Core model) → `TriggerRecompute()`. `Move` corrects the target index for same-collection moves and
verifies the target isn't the node's own descendant (`IsDescendantOf`).

`PromoteLevel`/`DemoteLevel` are both, under the hood, special cases of `Move` (re-parented right
after the old parent's own position, or onto the tail of the previous sibling's children,
respectively).

`SetChildLevelCapAsync(node)` bulk-deletes everything deeper than the absolute level chosen in the
dialog via `TruncateBelowLevel`.

A newly added node immediately gets the currently-active "force Fit" / "force expand-state" overrides
applied to it (`ApplyCurrentOverridesToNewNode`). That application is treated as part of the add
operation itself, and doesn't become its own separate Undo snapshot (suppressed via
`_suppressUndoSnapshots`).

### 2.3 Editing the pre-merge page number

Editing `BookmarkNodeViewModel.PreOffsetPageNumber` calls
`BookmarkTreeViewModel.OnPreOffsetPageNumberChanged(node, newValue)`, which:

1. Computes `delta = newValue - <pre-edit effective value>` (a no-op if it's 0).
2. Adds that delta onto `PageOffset` for every node in the same file (`SourceFileEntryId`) whose
   `OriginalPageIndex` is **at or after** the edited node's (position within the source PDF's own
   page structure — not the tree's display order).
3. Calls `TriggerRecompute()`.

`ResetFilePageNumbers(node)` resets `PageOffset` back to `null` for the **entire file**, rather than
just from that node onward — resetting node-by-node would leave earlier edits (on pages before the
reset target) intact. If the file has no active edits, it's a no-op and pushes no Undo snapshot.

The cascade into post-merge page numbers is handled by `ComputeCumulativeDeltaBeforeFile()`. For each
file it takes "the `PageOffset` of whichever node in that file has the largest `OriginalPageIndex`"
(the file's total effect on everything after it, since every edit's range always extends through
that file's last page), accumulates these along `_orderedFileIds`, and returns, per file, the sum of
every preceding file's total delta. This function is called from both
`RecomputeAllPageNumberDisplaysAsync` and `ToExportModel`; it's a read-only aggregation (not
chunked — it never writes back to properties, so its real-world cost is far below the write-back
loop's).

<a id="undo"></a>
### 2.4 Undo

`App/Undo/UndoHistory<T>` is a memory-budgeted stack: it accumulates each snapshot's estimated size
in bytes, and once the total exceeds a cap (100 MB by default) it discards the oldest entries first,
always keeping at least the newest one. `BookmarkTreeViewModel` uses it specialized to `string` (a
JSON serialization of `_rootModel`).

- `PushUndoSnapshot()` (no argument) — called right before a structural edit; always pushes exactly
  one entry, no coalescing.
- `PushUndoSnapshot(coalesceKey)` — used for property edits. Repeated calls with the same key within
  800ms (`SnapshotCoalesceWindow`) are treated as a single edit and don't push a new entry (this
  keeps the Undo history from ballooning by one entry per keystroke while typing).
- `Undo()` — JSON-deserializes the most recent snapshot and hands it to `RebuildTree` (the entry is
  popped and consumed; LIFO order).

Each `BookmarkNodeViewModel` property (`Title`/`IsOpen`/`DestinationType`/coordinates) skips its
constructor-time initial replay with `Skip(1)` before calling `RequestUndoSnapshot` on every
subsequent change. Without that `Skip(1)` (`ReactivePropertySlim.Subscribe` replays the current value
once right after subscribing), every node construction would push an Undo entry with no actual
change — and since Undo itself rebuilds the tree, that becomes an infinitely growing history.
Applying the "force Fit" / "force expand-state" overrides is a temporary display-level change (auto-
restored when turned off), not an "edit" that belongs in Undo, so it's suppressed via
`_suppressUndoSnapshots`.

### 2.5 `CanUndo`/`UndoCommand`'s CanExecute (since v1.2.1)

```csharp
var canUndo = CanUndo.CombineLatest(IsBusy, (canUndo, busy) => canUndo && !busy);
UndoCommand = new ReactiveCommand(canUndo);
```

While the large-node chunked recompute is running (`IsBusy`), the busy overlay blocking mouse input
is the primary defense, but `UndoCommand`'s own CanExecute also folds in `!IsBusy` as a second layer
(so that no future input path bypassing the overlay — a keyboard shortcut, say — could ever race
against an in-progress recompute, even in theory; added during code review).

<a id="recompute"></a>
### 2.6 Responsiveness on large bookmark sets — chunked processing (since v1.2.1)

<img src="../diagrams/recompute-flow.svg" alt="Chunked recompute and busy-overlay flow" width="100%" />

`RecomputeAllPageNumberDisplaysAsync()` (formerly `RecomputeAllPageNumberDisplays`; guarded
internally by `_isRecomputingPageNumbers` against recursing on its own write-back) runs after every
operation that can change the tree's structure or `PageOffset` values — load, Undo, add, remove,
level-cap truncation, edits — recomputing every node's `PreOffsetPageNumber`/
`DisplayMergedPageNumber`/`IsPageNumberEdited`, writing them back, and updating
`HasPageNumberEdits`/`HasPageNumberInconsistency`.

Only when the node count exceeds `RecomputeChunkSize` (200) does the write-back loop split into
chunks of that size, yielding via `await Task.Yield()` between chunks to hand control back to the UI
thread. `IsBusy`/`BusyProgress` are updated during this window, and `MainWindowViewModel` forwards
them into its own same-named properties — reusing the exact busy overlay and progress display already
built for file loading, with no new UI. Trees at or below the threshold never hit an `await` inside
the loop at all, and still complete fully synchronously exactly as before (avoiding needless overhead
and flicker on small trees).

The structural-edit methods (`AddRoot` etc.) stay synchronous with unchanged signatures by going
through `TriggerRecompute()`, an `async void` fire-and-forget wrapper:

```csharp
private async void TriggerRecompute() => await RecomputeAllPageNumberDisplaysAsync();
```

On a small tree, since nothing inside ever awaits, the work is effectively finished by the time this
method returns — so existing tests and code-behind that assume synchronous completion keep working
unmodified. On a large tree, the busy overlay covers the whole bookmark-editing screen and blocks
mouse input, so a second, overlapping call to this method (triggered by further user action) is not
expected to happen while one is already in flight.

<a id="expand-level"></a>
### 2.7 Bulk expand/collapse-by-level (since v1.2.2)

The logic behind the "-" button, level-number text box, and "+" button above the bookmark tree.

- `ExpandLevelInput` (`ReactivePropertySlim<string>`) — two-way bound to the text box. Whenever it
  changes to a valid number (a non-negative integer at or below the tree's current max level), every
  node at that level or shallower gets `IsExpanded=true`, and everything deeper gets
  `IsExpanded=false` (e.g. `"3"` expands levels 1-3 and collapses level 4+). The apply operation
  itself (`ApplyExpandLevelAsync`) reuses the exact same chunked-processing and
  `IsBusy`/`BusyProgress` framework as `RecomputeAllPageNumberDisplaysAsync` in [§2.6](#recompute),
  so bulk expand/collapse doesn't freeze the UI on a large bookmark tree either.
- `CollapseAllCommand` (the "-" button) — sets `ExpandLevelInput` to `"0"` (since every node's level
  is 1 or higher, `LevelNumber <= 0` is always false, so every node collapses).
- `ExpandAllCommand` (the "+" button) — sets `ExpandLevelInput` to the tree's current max level
  (guaranteeing every node's `LevelNumber` is at or below it, so every node expands). Both commands
  fold `!IsBusy` into their CanExecute, the same as `UndoCommand`.
- `NormalizeExpandLevelInput()` (public) — clears the text box to an empty string when its value is
  non-numeric, or a number not present in the tree (above the current max level). Called from
  code-behind on the text box's LostFocus, and also from inside `AddRoot`/`AddChild`/
  `AddSiblingAfter`/`Remove`/`Move`/`SetChildLevelCapAsync`/load/undo, so a structural bookmark edit
  that invalidates the current value clears it automatically too.

## 3. `BookmarkNodeViewModel`

The ViewModel for a single bookmark-tree node. Its `Title`/`IsOpen`/`DestinationType`/`Left`/`Top`/
`Right`/`Bottom`/`Zoom` `ReactivePropertySlim<T>`s each write straight through to the underlying
`Model` (`BookmarkNode`) on every change, while also asking `BookmarkTreeViewModel` to push an Undo
snapshot. Skipping the constructor-time initial replay (`Skip(1)`) matters here too, for the same
reason as [§2.4](#undo) above.

`PreOffsetPageNumber` is the one exception: it does **not** write straight to `Model`. How an edited
value should cascade (to later nodes in the same file, and to files after it) needs to be computed
collectively by `BookmarkTreeViewModel`, so this property only forwards the change notification
upward.

`IsDestinationTypeEditable`/`IsLeftEditable`/`IsTopEditable`/`IsZoomEditable` are derived properties
that enable only the coordinate controls actually meaningful for the currently selected destination
type (`XYZ`/`Fit`/`FitH`/`FitV`); all of them go false while `ForceFitForAll` is on.

## 4. `FileListViewModel`

The merge-target PDF file list (step 1). Handles adding via D&D/dialog (`AddPaths`), moving a
contiguous selected block up/down as a unit (`MoveSelectionUp`/`MoveSelectionDown`; a no-op if the
selection isn't contiguous), and D&D reordering (`MoveTo`). `GetMoveAvailability` returns both
buttons disabled when the selection is empty or non-contiguous.

## 5. Services

| Type | Role |
|---|---|
| `IDialogService` (implemented on the Wpf/Avalonia side) | Shows the file/folder pickers, save dialogs, and the properties/settings/level-cap dialogs |
| `IUserSettingsService` / `UserSettingsService` | Reads user settings (via `IOptionsMonitor`) and saves them atomically |
| `AppLanguageBootstrapper` | Settles the display language at startup; handles immediate switching from the Settings dialog |
| `AppPaths` | Resolves where settings and logs live |

## 6. Supporting ViewModels

`PdfFileEntryViewModel` (one row of the file list; holds `PageCount`/`LoadFailed`),
`PropertiesDialogViewModel` / `SettingsViewModel` / `LevelCapDialogViewModel` (each dialog's input as
a ViewModel, shared between WPF and Avalonia and constructed/disposed by the `IDialogService`
implementation). **Since v1.2.2**, `SettingsViewModel` also exposes `AppVersion` (string), shown in
the Settings dialog. Its value comes from `Assembly.GetExecutingAssembly()`'s
`AssemblyInformationalVersionAttribute`, which the build writes directly from
`Directory.Build.props`'s `<Version>`. `PdfPageSlotViewModel` (one placeholder per page in the
continuous-scroll preview) is covered in [§7.2](#link-editor-scroll).
`ShowMergeAndEditLinksButton` (`ReactivePropertySlim<bool>`) drives whether the "Merge and Continue
to Link Editing" button is shown. Unlike `ThemeMode`/`Language`, it never triggers a theme reapply or
a window rebuild — it's a plain binding target, so `MainWindowViewModel.OpenSettingsAsync` just writes
the new value right after the dialog is confirmed, and the XAML binding picks it up immediately.

<a id="link-editor"></a>
## 7. `LinkEditorViewModel`

Orchestrates step 5 (`WorkflowStep.EditLinks`). Targets a single, already-merged-and-bookmarked PDF
file: continuous-scroll page preview, zoom, jumping from the bookmark list, creating links via text
selection, and listing/verifying/deleting links. Registered in DI the same way as
`MainWindowViewModel` (constructor: `IPdfPageRenderer`, `IPdfTextExtractor`, `IPdfMetadataService`,
`IPdfLinkAnnotationService`, `ILogger<LinkEditorViewModel>`).

### 7.1 `LoadAsync` — initializing on screen entry

`LoadAsync(filePath, ct)`:

1. Reads page count and the bookmark list via `IPdfMetadataService.ReadMetadataAsync` (the merged
   file can be treated as "a single PDF that has bookmarks," so the multi-file-specific
   `SourceFileEntryId`/`OriginalPageIndex` machinery isn't needed here).
2. Reads page 0's PDF-user-space size, used to size the continuous-scroll placeholders
   (see [§7.2](#link-editor-scroll)).
3. Creates `PageSlots` (below), one per page.
4. Only if the file actually exists on disk (unit tests sometimes pass a fictional path): copies it
   to a "pristine" backup in the temp folder (see [§7.5](#link-editor-finish)), then reads links
   already present via `IPdfLinkAnnotationService.ReadExistingLinksAsync` and adds them to `Links`
   (see [§7.6](#link-editor-existing-links)).

<a id="link-editor-scroll"></a>
### 7.2 Continuous-scroll preview — `PageSlots` and virtualization

The preview is designed as a virtualized list of lightweight placeholders, specifically so a
multi-thousand-page PDF never holds every page's bitmap in memory at once.

- `PageSlots` (`ObservableCollection<PdfPageSlotViewModel>`) — one lightweight placeholder per page,
  holding `PageIndex`, `Image` (`byte[]?`, null until rendered), and `IsCurrent` (bool — whether this
  page is the current target of selection/link-creation/hotspot display). `LoadAsync` creates one per
  page up front (no rendering involved).
- `LoadPageSlotAsync(pageIndex)` — called from the UI when a virtualized container is realized. If
  that slot hasn't been rendered yet, renders the page and assigns it to `Image`. A per-page-index
  `CancellationTokenSource` dictionary handles repeat/overtaking calls for the same page.
- `UnloadPageSlot(pageIndex)` — called when a container leaves the viewport (recycled by the
  virtualizing panel). Cancels an in-flight render if any, and resets `Image` to `null` to free the
  memory — this is the piece that keeps memory bounded on a multi-thousand-page document.
- `PlaceholderWidth`/`PlaceholderHeight` — the pixel width/height reserved for an unrendered slot at
  the current zoom. Page 0's PDF-user-space size stands in for every page (fetching every page's real
  size up front would be expensive on a large PDF), recomputed via
  `PdfCoordinateMapper.PixelsPerPoint(scale)` on every zoom change.

See [04-ui-design.md §6.2](04-ui-design.md#link-editor-scroll-ui) for how the UI side derives
"whichever page occupies the largest area of the viewport" from scroll position and writes it to
`CurrentPageIndex`.

### 7.3 Keeping `CurrentPageIndex` and `PageNumberInput` in sync

Whenever `CurrentPageIndex` (0-based) changes, `OnCurrentPageIndexChanged`:

1. Clamps an out-of-range value to the nearest valid one (a self-reentrant set;
   `ReactivePropertySlim` never re-notifies on an unchanged value, so this converges).
2. Flips the old `_currentSlot`'s `IsCurrent` to false and the new slot's to true.
3. Syncs `PageNumberInput` (1-based, two-way bound to the page-turn toolbar's text box).
4. Fetches the current page's metadata (`PageHeight`, and `Letters` only if the page actually
   changed) fire-and-forget (`TriggerLoadCurrentPageMetadata`) — the page's bitmap itself is
   `PageSlots`' job, not handled here.

Changes from the other direction — the user typing directly into `PageNumberInput` — also flow back
into `CurrentPageIndex` with the same clamping (`OnPageNumberInputChanged`), so the two stay
synchronized both ways.

### 7.4 Creating links via text selection

- `BeginTextSelection`/`UpdateTextSelection`/`EndTextSelection` — take the PDF-user-space coordinates
  of a drag's start/move/end, hit-testing them against the current page's `Letters` (the character
  rectangles `PdfTextExtractor` extracted) to find a selection range (a span of character indices). If
  no character's rectangle actually contains the point, the nearest one by center-to-point distance is
  used instead, so a drag that strays slightly outside a character's bounds doesn't break selection.
- On commit, `GroupLettersIntoLineRects` groups the selected characters by line (a gap of 2pt or more
  between adjacent characters' Bottom coordinates counts as a line break) and computes each line's
  bounding rect, populating `PendingSelection` (`SourcePageIndex` + a list of per-line rects). A
  selection spanning multiple lines is kept as multiple rects (since a PDF Link annotation's `/Rect`
  can only be a single rectangle).
- `CreateLinkToBookmark(bookmark)` — for each line rect in `PendingSelection`, creates a
  `LinkAnnotationNode` that copies the chosen bookmark's `DestinationType`/coordinates verbatim and
  adds it to `Links` (multiple lines become multiple links sharing one `GroupId`).
- `PickArbitraryTargetAndCreateLink(targetPageIndex, pdfX, pdfY)` — while
  `IsPickingArbitraryTarget` is on, commits a link the same way using the clicked preview position as
  an XYZ destination.
- `LinkGroups` (`ReactivePropertySlim<IReadOnlyList<LinkGroupInfo>>`) — a summary of `Links` grouped
  by `GroupId`, for the list UI. Recomputed on every `Links.CollectionChanged`.

### 7.5 `DeleteLinkGroup` / `BeginEditLinkGroup`

`DeleteLinkGroup(groupId)` removes every link with that `GroupId` from `Links`.
`BeginEditLinkGroup(groupId)` removes them the same way, then restores the same hotspot (`SourceRect`
set) into `PendingSelection` — so `CreateLinkToBookmark`/`PickArbitraryTargetAndCreateLink` can be
reused as-is to pick a new destination (the links get a fresh `GroupId` on commit; `GroupId` is purely
an internal aggregation key with no requirement to stay stable across an edit). Both are no-ops when
any of the targeted links is a pre-existing one (see [§7.6](#link-editor-existing-links)).

<a id="link-editor-finish"></a>
### 7.6 `FinishAsync` — completion (writing links) and idempotency

<a id="link-editor-existing-links"></a>
`PdfLinkAnnotationService.ApplyLinksAsync` only ever **appends** in `Modify` mode — it has no way to
safely delete or replace an existing annotation (see
[02-core-design.md §2.11](02-core-design.md#link-editor-services)). That constraint drives two design
decisions in `LinkEditorViewModel`:

1. **A "pristine" backup and an idempotent Finish**: right after `LoadAsync` (before this session has
   applied any links at all), the file is copied to a temp-folder backup held as
   `_pristineBackupPath`. `FinishAsync` always restores that backup onto `FilePath` first, then calls
   `ApplyLinksAsync` — so pressing "Finish" more than once (even as `Links`' contents keep changing
   between presses) never duplicates an annotation.
2. **Excluding pre-existing links**: links read via `ReadExistingLinksAsync` in `LoadAsync` get their
   Ids recorded in `_preExistingLinkIds` (a `HashSet<Guid>`) before being added to `Links` (so they
   show up in the list). Since restoring the pristine backup already brings them back, `FinishAsync`
   sends `ApplyLinksAsync` only the links in `Links` that **aren't** in `_preExistingLinkIds` — the
   newly created ones. Sending the pre-existing ones too would write them a second time. For the same
   reason, `DeleteLinkGroup`/`BeginEditLinkGroup` (§7.5) no-op on pre-existing links — there's no way
   to make a deletion or a re-targeted destination "stick" for them. The UI reads
   `LinkGroupInfo.IsPreExisting` (true when every link in a group is in `_preExistingLinkIds`) to hide
   the edit/delete buttons for those entries
   (see [04-ui-design.md §6.4](04-ui-design.md#link-editor-existing-links-ui)).

<a id="link-editor-thread"></a>
### 7.7 Two bugs found during implementation

- **Cross-thread crash (`ConfigureAwait(false)`)**: early on, `LoadAsync` and other async methods
  used `.ConfigureAwait(false)`, so the continuation after the first `await` ran on a thread-pool
  thread — and the moment it wrote to a `ReactivePropertySlim` like `IsBusy`, WPF's `CommandManager`
  (UI-thread-only) threw `InvalidOperationException` (cross-thread access). No other App-layer
  ViewModel (`BookmarkTreeViewModel` etc.) uses `ConfigureAwait(false)` at all, so this simply broke
  from that existing convention. Fixed by removing it everywhere in the file
  (see [01-architecture.md §4.5](01-architecture.md)).
- **Scroll-driven current-page detection failing**: the first implementation used
  `VisualTreeHelper.HitTest` against the element at the viewport's top edge to identify the page
  there. On the WPF build, `Wpf.Ui`'s `FluentWindow` silently swaps the internal `ScrollViewer` for
  its own `PassiveScrollViewer`, and hit-testing against it never resolved past the control itself
  into its actual content (found by adding temporary diagnostic logging and inspecting what the hit
  actually was). Fixed by switching to deriving the page directly from the scroll offset and the
  known per-page height, given every page shares the same placeholder size — see
  [04-ui-design.md §6.2](04-ui-design.md#link-editor-scroll-ui).
