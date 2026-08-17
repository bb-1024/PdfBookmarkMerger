# 05. Design-Level Diffs Across Versions (v1.0.0 → v1.2.2)

This document was assembled by enumerating changed files with
`git diff --name-status <previous version> <target version> -- src tests`, then individually
inspecting the relevant commits (`git show <commit>`). The "How to verify this yourself" section at
the end gives the exact commands so anyone can reproduce the same process.

## Tags

| Tag | Date | Commit |
|---|---|---|
| `v1.0.0` | 2026-07-27 | `12611ca` |
| `v1.1.0` | 2026-08-01 | `4fc46b1` |
| `v1.2.0` | 2026-08-03 | `9822c18` |
| `v1.2.1` | 2026-08-12 | `b61d2ee` |
| `v1.2.2` | 2026-08-17 | `0bc7147` |

---

## v1.0.0 (2026-07-27) — Initial release

The first tagged release. Of the design described in
[00-overview.md](00-overview.md) through [04-ui-design.md](04-ui-design.md), the following
**did not exist yet** at this point (added in later versions):

- Japanese/English switching (i18n) — `AppLanguageBootstrapper` / the `Strings.resx` family were
  added in v1.1.0
- Undo — `UndoHistory<T>` was added in v1.1.0
- The busy overlay / progress display — `BusyProgressInfo` / `IsBusy` were added in v1.1.0
- Bookmark settings (XML) export — `BookmarkSettingsExportService` was added in v1.2.0
- Editing the pre-merge page number — `BookmarkNode.PageOffset` was added in v1.2.0

Features already present at v1.0.0: adding PDF files/folders via D&D, automatic bookmark extraction
(`PdfMetadataService`) with a filename fallback for bookmark-less PDFs (`MissingBookmarkFallback`),
basic bookmark-tree editing (title, destination type, coordinates, add/remove, D&D reordering,
level-cap truncation), PDF merging (`PdfMergeService`) with a properties-edit dialog, settings/log
persistence (Serilog daily rolling files), and global unhandled-exception logging.

---

## v1.1.0 (2026-08-01)

The busiest window of this project's history: 28 commits on top of v1.0.0, both in features added and
bugs fixed. `git diff --name-status v1.0.0 v1.1.0 -- src tests` shows 56 changed files.

### Features added

- **Japanese/English i18n** (`822a2d8`) — `Strings.resx`/`Strings.en.resx`, plus
  `AppLanguageBootstrapper` auto-detecting and persisting the language on first launch. Immediate
  switching from the Settings dialog followed later, in `7e49524` (unified with how theme mode was
  already handled).
- **Undo** (`b9ad0c5`) — added `UndoHistory<T>`, a memory-budgeted stack, to the bookmark-tree
  editor.
- **Faster bulk loading/merging + a busy overlay** (`6db6849`) — parallelized `ConfirmFilesAsync`
  (metadata reads) and `PdfMergeService` (opening PDFs) with bounded concurrency, and introduced a
  busy overlay that disables every control while processing, plus a detail-progress display
  (completed/total, in-flight file names) that appears after 5 seconds.
  **This "busy overlay + `BusyProgressInfo`" framework is exactly what v1.2.1 later reuses for
  `BookmarkTreeViewModel`'s chunked recompute** (see the [v1.2.1 section](#v121)).
- Promote/demote-level buttons (`57d531a`); including the node's own level as a level-cap dialog
  option (`1daaf72`); selecting a row by clicking its empty margin (`23f3d38`); auto-scroll near the
  tree's edges during D&D (`0b42ad5`).
- File list: moving a multi-selected block as a unit (`87c85ac`); disabling the move buttons at list
  boundaries (`65b6817`).
- Escape now cancels dialogs (`f8ac748`); the initial window was enlarged and the editor's buttons
  reordered (`1bf3602`).

### Bugs fixed (3 distinct "freeze" issues)

All three of these presented to users the same way — "the app just locks up" — but each had a
**different root cause**.

1. **A deadlock that left the window completely blank on first launch** (`28a4dce`) —
   `UserSettingsService.SaveAsync` awaited `File.WriteAllTextAsync` without `ConfigureAwait(false)`.
   On a genuinely first run, `App.OnStartup` calls
   `AppLanguageBootstrapper.ApplyAsync(...).GetAwaiter().GetResult()` synchronously, blocking the UI
   thread, before `MainWindow` is even constructed. Without `ConfigureAwait(false)`, the awaited
   continuation tried to resume on that very same (blocked) UI thread's `SynchronizationContext`,
   deadlocking forever: the process stayed alive, but `OnStartup` never returned and no window ever
   appeared. A leftover `settings.json.tmp` (the atomic-write temp file, never renamed because
   `File.Move` sat downstream of the stuck await) was direct evidence of exactly where execution had
   stopped. The fix was simply adding the missing `ConfigureAwait(false)`.
2. **A freeze while editing the bookmark tree, caused by NaN coordinates** (`87a2de7`) — PDFsharp's
   `PdfOutline.Left`/`Top`/`Right`/`Bottom`/`Zoom` return NaN when the destination type doesn't
   specify that particular coordinate. That NaN was being stored as-is on `BookmarkNode`, and every
   structural edit (`Move`/`PromoteLevel`/`DemoteLevel`/`SetChildLevelCapAsync`) calls
   `PushUndoSnapshotCore`, which JSON-serializes the tree — and `System.Text.Json` throws on
   non-finite doubles. That meant the very first edit on any realistically-extracted PDF threw an
   unhandled exception, presenting to the user as a freeze. The fix added `AsFiniteOrNull` to
   `PdfMetadataService`, normalizing non-finite values to `null` ("unspecified"), with regression
   tests at both the Core level and the App level (the App-level test is today's
   `BookmarkTreeEditFreezeReproTests` — it loads real sample PDFs through the same production Core
   services and verifies each edit operation completes within 10 seconds).
3. **The UI freeze on a large number of bookmarks** — this one was **not** fixed in this window; it
   was fixed later, in [v1.2.1](#v121), and is an entirely different bug: synchronous processing on
   trees with more than 200 nodes.

### The settings-file location, back and forth

`1ac63b8` (moved next to the executable, an attempt at a portable setup) was followed by `87a6bb4`
(moved to `%AppData%/PdfBookmarkMerger/`, alongside the logs). The executable's own folder could be
read-only, so the team settled on the AppData folder, where a write is always guaranteed to succeed.

### Other fixes

- `9805b89`: reverted an earlier attempt (`7892ff1`) to stretch a bookmark row's background so D&D
  hit-testing covered its full width — it turned out to cause an unwanted horizontal scroll jump on
  click. That lesson carried forward directly into [v1.2.1's horizontal-scroll fix](#v121).
- `2ad5394`: moved the Avalonia build's file/bookmark `PointerPressed` subscription to the Tunnel
  phase (working around `SelectingItemsControl`'s default selection handling claiming the event
  first, on the Bubble phase).
- `70f50dd`: guarantees any successfully-opened PDFs get disposed even if parallel opening partially
  fails.
- `41286b1`: fixed duplicate `SystemThemeWatcher` registration and a missed unwatch on window close.
- `5de9ab9`: gave `Right`/`Bottom` coordinates the same Undo tracking `Left`/`Top`/`Zoom` already had.
- `e486342`: made the Avalonia build's save dialog default to the "Documents" folder, matching WPF.
- `5daeabd`: restricted Serilog's console sink to Debug builds only.
- `1077825`: wrapped Avalonia's entire main loop in `try/catch` so exceptions that escape the UI
  thread are still reliably logged (there's no Avalonia equivalent of WPF's
  `DispatcherUnhandledException`).

---

## v1.2.0 (2026-08-03)

4 commits, adding both the bookmark settings file export and pre-merge page-number editing.

- **`de060a2` Bookmark settings (XML) export** — introduced `IBookmarkSettingsExportService`/
  `BookmarkSettingsExportService`, letting the bookmark structure alone be exported as
  "bookmark settings file spec" XML without running a full PDF merge. Added
  `ShowSaveBookmarkSettingsDialogAsync` to `IDialogService`.
- **`0db8409` Made the pre-merge page number editable** — added `BookmarkNode.PageOffset` (int?), and
  the mechanism that cascades an edit to `BookmarkNodeViewModel.PreOffsetPageNumber` onto later nodes
  in the same file via `BookmarkTreeViewModel.OnPreOffsetPageNumberChanged`.
- **`8384e20` Auto-sizing width, highlighting, and a reset menu for the text box** — added
  `PageNumberWidthConverter` and `EditedHighlightBrushConverter` (both WPF and Avalonia).
- **`8a51402` Per-file page-number reset, and the bump to v1.2.0** — added `ResetFilePageNumbers(node)`.
  Resetting node-by-node would have left earlier edits (on pages before the reset target) intact, so
  the design resets the entire file's edits at once instead.

<a id="v121"></a>
## v1.2.1 (2026-08-12)

3 commits: two user-reported bug fixes, plus one additional hardening change from code review.

### `75296f1` Fixed the UI freeze on a large number of bookmarks

`RecomputeAllPageNumberDisplaysAsync` (which walks the whole tree twice on every pre-merge
page-number edit) ran fully synchronously, so editing/adding/removing/undoing on a large bookmark set
would occupy the UI thread long enough that not even an `IsBusy`-style indicator ever got a chance to
render — it looked frozen, with no progress update at all. The fix: only when the node count exceeds
`RecomputeChunkSize` (200) does the write-back loop split into chunks, yielding via
`await Task.Yield()` to hand control back to the UI thread while updating `IsBusy`/`BusyProgress`.

This fix **reuses the busy-overlay framework that v1.1.0 introduced for "file loading / PDF
merging" (`6db6849`)**, applying it to a different kind of background work — recomputing the bookmark
tree — carrying that v1.1.0 design forward directly (no new UI was needed; only a forwarding
subscription from `MainWindowViewModel.IsBusy`/`BusyProgress` was added). See
[03-app-design.md §2.6](03-app-design.md#recompute) for the detailed design.

### `0a231ab` Preserving horizontal scroll position on a bookmark-row click

Because each bookmark-tree row is wide, clicking a row while the horizontal scrollbar was showing
used to trigger both WPF's and Avalonia's default "bring into view" behavior and move the horizontal
scroll position unexpectedly. Fixed by capturing the horizontal position right after the click and
restoring it once selection/focus-change processing has settled, at a low-priority dispatch point.
This is **effectively a second attempt at the same class of horizontal-scroll problem that v1.1.0's
`9805b89` had backed away from**. See
[04-ui-design.md §2.3](04-ui-design.md#scroll-fix)
for the detailed design.

### `a7ee360` Code review: also gate `UndoCommand`'s CanExecute on `IsBusy`

While the chunked recompute above is running, the busy overlay blocking mouse input was the primary
defense; this change adds a second layer by also folding `!IsBusy` into `UndoCommand`'s own
CanExecute (closing off, even in theory, any future input path that could bypass the overlay and race
against an in-progress recompute).

---

<a id="v122"></a>
## v1.2.2 (2026-08-17)

1 commit (`e044f98`): two user-requested feature additions.

### Bulk expand/collapse-by-level controls for the bookmark editor tree

Added a "-" button, a level-number text box, and a "+" button directly above the bookmark tree.
Entering a number N into the new `BookmarkTreeViewModel.ExpandLevelInput` (string) expands every
node at level N or shallower (`IsExpanded = true`) and collapses everything deeper
(`IsExpanded = false`) — e.g. N=3 expands levels 1-3 and collapses level 4+. The "-" button
(`CollapseAllCommand`) sets `ExpandLevelInput` to `"0"`; the "+" button (`ExpandAllCommand`) sets it
to the tree's current max level — both reuse the exact same apply logic instead of duplicating it.
A non-numeric value, or a number not present in the tree, is normalized back to an empty string by
`NormalizeExpandLevelInput`, either when the text box loses focus or when a structural bookmark edit
(add/remove/move/level-cap truncation/load/undo) makes the current value fall out of range.

**The bulk-apply operation (`ApplyExpandLevelAsync`) reuses the exact same chunked-processing and
`IsBusy`/`BusyProgress` framework that v1.2.1 introduced for `RecomputeAllPageNumberDisplaysAsync`**,
so expanding/collapsing a very large bookmark tree doesn't freeze the UI either. See
[03-app-design.md §2.7](03-app-design.md#expand-level) and
[04-ui-design.md §2.6](04-ui-design.md#expand-level-controls) for the detailed design.

### Showing the release version in the Settings dialog

Added `SettingsViewModel.AppVersion` (string), displayed at the bottom of the Settings dialog. Its
value comes from `Assembly.GetExecutingAssembly()`'s `AssemblyInformationalVersionAttribute`, which
the build writes directly from `Directory.Build.props`'s `<Version>` (both the WPF and Avalonia
builds share the same `Directory.Build.props`, so `App.dll`'s version is representative of either).

---

## How to verify this yourself

```bash
# tags and their commit dates
git tag -l --sort=v:refname
git log -1 --format="%ad %s" --date=short v1.1.0

# files changed between versions
git diff --name-status v1.0.0 v1.1.0 -- src tests
git diff --name-status v1.1.0 v1.2.0 -- src tests
git diff --name-status v1.2.0 v1.2.1 -- src tests
git diff --name-status v1.2.1 v1.2.2 -- src tests

# every commit within each version window
git log --oneline v1.0.0..v1.1.0 -- src tests
git log --oneline v1.1.0..v1.2.0 -- src tests
git log --oneline v1.2.0..v1.2.1 -- src tests
git log --oneline v1.2.1..v1.2.2 -- src tests

# a specific commit's full diff
git show <commit-hash>

# current test counts, per project
dotnet test tests/PdfBookmarkMerger.Core.Tests/PdfBookmarkMerger.Core.Tests.csproj --list-tests
dotnet test tests/PdfBookmarkMerger.App.Tests/PdfBookmarkMerger.App.Tests.csproj --list-tests
dotnet test tests/PdfBookmarkMerger.UiConverters.Tests/PdfBookmarkMerger.UiConverters.Tests.csproj --list-tests
```
