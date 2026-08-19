# PdfBookmarkMerger Design Documentation — 00. Overview

> **About this documentation**
> Grounded in the **actual source code and commit history** of the tagged versions present in the
> repository (`v1.0.0` / `v1.1.0` / `v1.2.0` / `v1.2.1` / `v1.2.2` / `v1.2.3`). Every claim about which version
> something belongs to can be traced individually in
> [05-version-history.md](05-version-history.md). The Japanese edition lives under `docs/ja/`
> with the same file structure.

## 1. What this app does

**PdfBookmarkMerger** is a desktop app that merges multiple PDF files into one while automatically
extracting and letting you manually edit the merged PDF's bookmarks (outline). It ships two
frontends — a **WPF (WPF-UI) build** for Windows and an **Avalonia build** for Windows/macOS — on
top of a shared domain layer and ViewModel layer.

Key features:

- Specify merge-target PDF files (drag & drop, or file/folder picker dialogs) and reorder them
- Automatic bookmark extraction from each PDF, with an automatic filename-bookmark fallback for PDFs
  that have none
- A bookmark-tree editor (title, destination type, coordinates, open/closed state, hierarchy level,
  ordering) with Undo support
- Direct editing of pre-merge page numbers, cascading to the affected page and every following file
- Merging/saving the PDF, and exporting the bookmark settings alone as an XML file
- **Link editing** (when enabled in settings): a screen that previews the merged,
  bookmarked PDF and lets you select text in the body to create, verify, and delete links —
  either to a bookmark's destination or to an arbitrary coordinate. The preview scrolls
  continuously (virtualized, so it never renders every page at once even for a
  multi-thousand-page document), and links already present in the file are also listed.
  See [03-app-design.md §7](03-app-design.md#link-editor) and
  [04-ui-design.md §6](04-ui-design.md#link-editor-ui) for details
- Japanese/English UI switching, light/dark/system theme

## 2. Tech stack

| Category | Detail |
|---|---|
| Runtime | .NET 10 |
| WPF frontend | WPF-UI (`Wpf.Ui.Controls.FluentWindow`) |
| Avalonia frontend | Avalonia (Fluent theme, `WithInterFont()`) |
| MVVM foundation | [Reactive.Bindings](https://github.com/runceel/ReactiveProperty) (`ReactivePropertySlim<T>`, `ReactiveCommand`, `AsyncReactiveCommand`) |
| DI / hosting | `Microsoft.Extensions.Hosting` Generic Host + `Microsoft.Extensions.DependencyInjection` |
| Configuration | `Microsoft.Extensions.Configuration` (two-tier: `appsettings.json` + a user settings file) |
| Logging | Serilog (daily rolling files; console sink added only in Debug builds) |
| PDF processing | [PDFsharp](https://github.com/empira/PDFsharp) 6.2.4 (structural read/write) + [PDFtoImage](https://github.com/sungaila/PDFtoImage) 5.4.0 (page rendering via PDFium) + [UglyToad.PdfPig](https://github.com/UglyToad/PdfPig) 1.7.0-custom-5 (character-level text extraction) |
| Testing | xUnit + [Shouldly](https://github.com/shouldly/shouldly) |

## 3. Project layout (as of v1.2.3)

```
src/
  PdfBookmarkMerger.Core/       Domain layer (models, PDF I/O, merge logic; UI-independent)
  PdfBookmarkMerger.App/        ViewModel layer and shared app services (UI-independent, wires up DI)
  PdfBookmarkMerger.Wpf/        WPF-UI frontend (net10.0-windows)
  PdfBookmarkMerger.Avalonia/   Avalonia frontend (net10.0)
tests/
  PdfBookmarkMerger.Core.Tests/         Core-layer tests (46)
  PdfBookmarkMerger.App.Tests/          App-layer ViewModel tests (120)
  PdfBookmarkMerger.UiConverters.Tests/ Golden tests exercising both WPF and Avalonia converters (36)
  sample/                                Real sample PDFs for manual/regression testing
tools/
  PdfBookmarkMerger.SampleGenerator/    Helper tool that generates the PDFs under tests/sample
scripts/
  publish-wpf-release.ps1               Builds the WPF release (self-contained, single-file)
```

Dependencies flow in one direction (`Wpf`/`Avalonia` → `App` → `Core`); neither `Core` nor `App`
depends on any UI framework. See [01-architecture.md](01-architecture.md) for details.

## 4. The app's basic flow

`MainWindowViewModel.Step` (a `WorkflowStep` enum) drives screen transitions.

1. **Select files** (`WorkflowStep.SelectFiles`) — add PDFs via drag & drop or dialogs and settle
   their order; this order becomes the merge order.
2. **Extract bookmarks** (right before entering `WorkflowStep.EditBookmarks`) — `ConfirmFilesCommand`
   reads every file's metadata in parallel, then extracts bookmarks and computes post-merge page
   numbers.
3. **Edit bookmarks** (`WorkflowStep.EditBookmarks`) — edit the extraction result as a tree.
4. **Merge & save** — there are two buttons here. "Merge and Save PDF" (`MergeCommand`) ends the
   workflow. "Merge and Continue to Link Editing" (`MergeAndEditLinksCommand`, shown only when
   enabled in settings — hidden by default) carries the merged file straight into step 5. Both
   share the same underlying merge (save-path dialog → properties dialog if enabled →
   `PdfMergeService.MergeAsync`).
5. **Edit links** (`WorkflowStep.EditLinks`, optional) — add, verify, and delete links on the file
   merged in step 4. See [03-app-design.md §7](03-app-design.md#link-editor) for details.

## 5. Document structure

| File | Content |
|---|---|
| [01-architecture.md](01-architecture.md) | Layer structure, DI wiring, cross-cutting concerns (logging, i18n, Undo, busy/progress) |
| [02-core-design.md](02-core-design.md) | Core layer: detailed design of models and domain services |
| [03-app-design.md](03-app-design.md) | App layer: detailed design of ViewModels and app services |
| [04-ui-design.md](04-ui-design.md) | WPF/Avalonia frontend detailed design (code-behind, converters) |
| [05-version-history.md](05-version-history.md) | Design-level diffs across v1.0.0 → v1.1.0 → v1.2.0 → v1.2.1 → v1.2.2 → v1.2.3, and how to verify them |

## 6. How this document is grounded

The content here was verified against the actual repository state as follows:

- Current-design descriptions: read directly from the relevant source file at HEAD (`v1.2.3`)
- Cross-version diff descriptions: enumerated with
  `git diff --name-status <previous tag> <target tag> -- src tests`, then each diff's content was
  individually inspected
- Test counts: the actual output count of `dotnet test <project>.csproj --list-tests` at HEAD

If you want to re-verify any of this yourself, run the command examples at the end of
[05-version-history.md](05-version-history.md).
