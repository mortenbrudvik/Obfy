# UI Backlog

Findings from a full review of the WPF desktop app (`Src/Obfy.UI`). All 35 items are fixed
on `fix/ui-backlog`. The sections below keep the original problem statements.

- **Reviewed:** 2026-11-20, commit `7805545` (v2.0.0), Debug build
- **Fixed:** `fix/ui-backlog`, merged to `main`. Product version stays 2.0.0.
- **Checked:** `dotnet test Tests/Obfy.UI.Tests` passed, including symbol-tree keys from the
  real renamer and Custom-preset restore. `SymbolMapKeyTests` passed. The app started.
  FlaUI `Category=UI` was not run, and the 1400×900 / 900×600 row counts in UI-008 were not
  re-measured.
- **Method:** read every XAML view, view model, converter and UI service; then ran the app and
  drove it through a real two-assembly (closed-set) obfuscation with UI Automation, capturing
  the window at the default size (1400×900) and the minimum size (900×600), dark theme, 150% DPI
- **Not covered by the original review:** light theme, high-contrast mode, screen-reader
  walkthrough, drag-and-drop by hand, source-file (`.cs`) and merge runs, error paths

## How to read this

Each item has:

- **Status** — Done
- **Priority** — P1 (wrong or broken behaviour a user will hit), P2 (clear usability cost),
  P3 (polish or cleanup)
- **Evidence** — *Observed* (seen in the running app), *Code* (follows from reading the code,
  not exercised), or *Unconfirmed* (suspected; reproduced from the code before fixing)
- **Where** — paths are relative to the repo root, as of the review commit

UI-011 was fixed by shrinking the toolbar, not by moving buttons into the title bar.
UI-022 uses an indeterminate bar. It does not add pipeline progress.

## Index

| ID | Priority | Status | Area | Title |
|----|----------|--------|------|-------|
| UI-001 | P1 | Done | Results | Symbol tree never groups members under their type |
| UI-002 | P1 | Done | Output | Log timestamp and level columns collide |
| UI-003 | P1 | Done | Accessibility | Status bar message is hidden from screen readers |
| UI-004 | P1 | Done | Files | Output directory preference is polluted and never validated |
| UI-005 | P1 | Done | Help | Help dialog clips at the minimum window size |
| UI-006 | P1 | Done | Help | Help text is out of date |
| UI-007 | P1 | Done | Files | Output directory placeholder is wrong for closed-set runs |
| UI-008 | P1 | Done | Layout | Files, Results and Output do not fit in one column |
| UI-009 | P2 | Done | Layout | Results panel cannot be dismissed |
| UI-010 | P2 | Done | Layout | Empty-state drop zone is clipped at the minimum size |
| UI-011 | P3 | Done | Layout | Title bar and toolbar use two rows of chrome |
| UI-012 | P2 | Done | Settings | Collapsed expanders do not show whether a technique is on |
| UI-013 | P2 | Done | Settings | Obfuscation level dropdown text is truncated |
| UI-014 | P2 | Done | Settings | Disabled Obfuscate button gives no reason |
| UI-015 | P2 | Done | Settings | Runtime profile does not affect the toggles it gates |
| UI-016 | P3 | Done | Settings | Options are grouped under the wrong expanders |
| UI-017 | P3 | Done | Settings | Protection toggles have no descriptions |
| UI-018 | P3 | Done | Settings | Signing key file has no Browse button |
| UI-019 | P3 | Done | Settings | Some settings have no UI at all |
| UI-020 | P3 | Done | Settings | Label casing and wording are inconsistent |
| UI-021 | P3 | Done | Settings | Choosing a preset silently overwrites Custom settings |
| UI-022 | P2 | Done | Progress | Progress bar sits at 0% for closed-set and merge runs |
| UI-023 | P2 | Done | Files | File status icons are monochrome and rows hide the output path |
| UI-024 | P2 | Done | Toolbar | Open output folder is always enabled and its icon is ambiguous |
| UI-025 | P3 | Done | Files | File list has no keyboard or bulk actions |
| UI-026 | P3 | Done | Files | Files can only be dropped on the Files panel |
| UI-027 | P3 | Done | Files | Typed output directory may be ignored on Ctrl+Enter |
| UI-028 | P3 | Done | Files | Drop overlay may flicker while dragging |
| UI-029 | P2 | Done | Results | Copy is enabled with nothing selected |
| UI-030 | P2 | Done | Results | Statistics strip does not add up to the total |
| UI-031 | P3 | Done | Results | Symbol search rebuilds the whole tree on every keystroke |
| UI-032 | P3 | Done | Results | Preview shows only the last output |
| UI-033 | P3 | Done | Output | Log is cleared at the start of every run |
| UI-034 | P3 | Done | Output | Log lines cannot be copied individually and never wrap |
| UI-035 | P3 | Done | Cleanup | Dead code and unused dependencies |

---

## Bugs

### UI-001 — Symbol tree never groups members under their type

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:240` (`ParseSymbol`),
  `Src/Obfy.Core/Obfuscators/Assembly/SymbolRenamingObfuscator.cs:76,102,115,132,150,710`,
  `Tests/Obfy.UI.Tests/ViewModels/ResultsViewModelTests.cs:61,82`

**Problem.** `ParseSymbol` splits symbol-map keys on `::` and treats the left side as the type.
Core never emits that format. It writes `Kind:Namespace.Type.Member`, for example
`Field:Settings.Core.SettingsServiceBase`1._filePath` and
`Method:Ns.Type.Name(signature)`. Nothing in `Src` produces `::`.

As a result every key falls through to the "no type name" branch:

- every row is a root node; nothing is nested
- every row gets the Type icon, including fields, methods and properties
- the raw `Field:` / `Method:` prefix is shown as part of the name
- a type's own rename is not associated with its members

The unit tests pass because they feed `MyType::Foo()`-style keys that only exist in the tests.

**Suggested fix.** Parse the real format: take the kind from the prefix before the first `:`,
then split the remainder into type and member (for methods, split before the `(`; watch for
nested types and generic arity such as `` `1 ``). Map `Type`, `Method`, `Field`, `Property`,
`Event`, `Namespace` and `Parameter` to `SymbolType` (add `Event` to the enum and to
`SymbolTypeToIconConverter`). Consider moving the key format into one shared helper in Core so
`ReportService` (`ReportService.cs:197`, `:443`) and the UI cannot drift apart again.

**Done when.** A real run shows types as parents with their members nested and correctly
iconed, and the tests use keys produced by the actual renamer.

---

### UI-002 — Log timestamp and level columns collide

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/OutputPanel.xaml:64-72`

**Problem.** The timestamp column is a fixed 85px. `HH:mm:ss.fff` in 12px Cascadia Mono needs
slightly more, so the text runs straight into the level: `17:09:51.559Info`.

Turning **Timestamps** off collapses the `TextBlock` but not the column, so an 85px empty
gutter stays on the left of every line.

**Suggested fix.** Make the timestamp column `Auto` with a right margin on the text, so it
sizes to its content and collapses to zero when hidden. Use `Grid.IsSharedSizeScope` /
`SharedSizeGroup` if the columns need to stay aligned across rows.

**Done when.** There is a visible gap between timestamp and level, and hiding timestamps moves
the level column to the left edge.

---

### UI-003 — Status bar message is hidden from screen readers

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed (UI Automation returned `Status` after a completed run)
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:197-201`

**Problem.** The status `TextBlock` sets `AutomationProperties.Name="Status"`. For a text
element the automation name *is* what gets read, so this replaces the message. The element is
also a polite live region, so every change announces "Status" and never the actual text
("Obfuscation complete", "Cancelled", …). It also means FlaUI tests cannot read the status
through `Name`.

**Suggested fix.** Remove the `AutomationProperties.Name` so the name falls back to the bound
text, and keep `LiveSetting="Polite"`. If a label is wanted, use
`AutomationProperties.LabeledBy` or put "Status: " in a separate element.

**Done when.** UI Automation reports the current status message as the element's name, and an
automation test asserts it after a run.

---

### UI-004 — Output directory preference is polluted and never validated

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed (the reviewer's `%APPDATA%\Obfy\ui-preferences.json`
  pointed at a deleted `%TEMP%\obfy-ui-obf-…\out` folder)
- **Where:** `Src/Obfy.UI/ViewModels/FilesViewModel.cs:170-177` (`ApplyStartup`),
  `:297-304` (`OnOutputDirectoryChanged`), `:93-105` (`ApplyPreferences`),
  `Src/Obfy.UI/Services/SettingsService.cs:13`,
  `Tests/Obfy.UI.AutomationTests/ObfuscateFlowTests.cs:33`

**Problem.** Three things combine:

1. `ApplyStartup` assigns `OutputDirectory` from a `-o` argument without suppressing the
   preference save, so a one-off command-line override becomes the saved default.
2. `ObfuscateFlowTests` launches the real app with `-o <temp dir>`, and the preferences path is
   hard-coded to the user's real `%APPDATA%\Obfy`. Running the UI automation tests therefore
   overwrites the developer's own preference with a temp folder that the test then deletes.
3. On startup the saved path is restored without checking it exists. The next run silently
   recreates the stale folder and writes output there.

**Suggested fix.**

- Treat `-o` as a session override: set it under `_suppressPreferenceSave` so it is not
  persisted (and make sure `PersistPreferencesAsync` on shutdown does not write it either).
- Let the preferences location be overridden (environment variable or constructor argument on
  `SettingsService`) and point the automation tests at a temp location.
- When the restored directory does not exist, either clear it or show it with a warning state
  rather than using it silently.

**Done when.** Running `Category=UI` tests leaves the real preferences file untouched, `-o`
does not change the saved default, and a missing saved directory is surfaced to the user.

---

### UI-005 — Help dialog clips at the minimum window size

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/HelpDialogContent.xaml:11-12`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:802-811`

**Problem.** The help content is fixed at 840×480 with matching minimums, inside a dialog set
to 920×640. The window's minimum is 900×600. At that size the article text is cut off on the
right edge mid-word and the dialog overlaps the title bar.

Two smaller layout issues in the same dialog: there is no gap between the topic list and the
article (the title touches the selection highlight), and the Close button spans the full width.

**Suggested fix.** Drop the fixed `Width`/`Height`/`MinWidth`/`MinHeight` and let the content
stretch; cap the dialog with `DialogMaxWidth`/`DialogMaxHeight` only, sized from the owner
window. Add a left margin to the article column.

**Done when.** At 900×600 all help text wraps inside the dialog and nothing overlaps the title
bar.

---

### UI-006 — Help text is out of date

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed (text compared against the running UI)
- **Where:** `Src/Obfy.UI/Help/HelpCatalog.cs:19-31`

**Problem.**

- Line 27 quotes the empty state as "Drag and drop assemblies here" / "or click Add Files
  (.dll, .exe, .cs)". The UI now says "Drag and drop assemblies or a solution" with the list
  `.dll, .exe, .cs, .sln, .slnx, .csproj, .vbproj, .fsproj`.
- Lines 19 and 26 say Add Files and drop accept `.dll`, `.exe` and `.cs`. Solutions and project
  files are also accepted and are not mentioned anywhere.
- Closed-set behaviour (two or more assemblies, or anything loaded from a solution) is not
  described at all, including skipped rows and their reasons.
- Line 21 says an empty Output Directory writes next to the input as `*.obfuscated.*`. That is
  not true for closed-set runs (see UI-007).

**Suggested fix.** Rewrite the "Getting started" and "Files & output" topics to cover
solutions/projects, closed-set runs, skipped rows and the real output locations. Avoid quoting
UI strings verbatim; if a string must be quoted, share a constant with the view so a test can
catch drift (the way `ConfidentialitySentence` is already shared).

**Done when.** Every statement in Help matches current behaviour, and no help text quotes a
string that does not exist in the UI.

---

### UI-007 — Output directory placeholder is wrong for closed-set runs

- **Status:** Done
- **Priority:** P1 · **Evidence:** Code (placeholder observed; `obfy-out` path read from code)
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:144`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:486-494`

**Problem.** The empty Output Directory box shows "Next to input as *.obfuscated.*". With two
or more assemblies, or anything from a solution, the run is a closed set and output goes to an
`obfy-out` folder next to the first input instead. The user is told one location and gets
another, and only finds out from a log line.

**Suggested fix.** Bind the placeholder to a view-model property that reflects the mode the
current file list will run in (single / closed-set / merge), for example "obfy-out folder next
to the first input" for closed sets.

**Done when.** The placeholder always describes where output will actually go for the current
file list.

---

## Layout

### UI-008 — Files, Results and Output do not fit in one column

- **Status:** Done
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:148-185`,
  `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:127-155`,
  `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml`

**Problem.** The right-hand column stacks three panels. Output is fixed at 200px
(`MainWindow.xaml:152`). Files carries about 190px of fixed chrome (header, Output Directory,
symbol-map toggle). Results carries about 170px (header, statistics strip, search box, tab
strip). Whatever is left is shared by the file list and the symbol tree.

- **Default size (1400×900), after a run:** the symbol tree shows about 2.5 rows and the file
  list fewer than 2.
- **Minimum size (900×600), after a run:** the file list is gone entirely, the Output
  Directory box and the symbol search box are each cut in half, and the tree is not visible.

The results are the payoff of a run and currently the hardest thing to see.

**Suggested fix (recommended).** Turn the bottom region into tabs — **Output | Results |
Preview** — so results get the full bottom area instead of competing with the file list, and
switch to the Results tab when a run finishes. Move Output Directory and the symbol-map toggle
out of the Files panel (a compact row under the toolbar, or the Settings panel) so the file
list gets that space back.

**Alternative.** Keep the bottom Output panel and switch the centre area between Files and
Results.

**Done when.** At 1400×900 the symbol tree shows at least ten rows after a run, and at 900×600
no control is clipped in either the before-run or after-run state.

---

### UI-009 — Results panel cannot be dismissed

- **Status:** Done
- **Priority:** P2 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:190`,
  `Src/Obfy.UI/Views/MainWindow.xaml:160-175`

**Problem.** `ShowResultsPanel` is set to `true` after every run and only reset when the next
run starts. There is no close button, so the file list stays squeezed until the user runs
again. It is also shown when every file failed and there is nothing to show.

**Suggested fix.** Largely solved by UI-008. If the stacked layout stays, add a close/collapse
button to the results header and do not show the panel when there are no successful results.

---

### UI-010 — Empty-state drop zone is clipped at the minimum size

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:53-59`

**Problem.** At 900×600, before any run, the drop zone is about 85px tall. The icon is cut off
at the top and the list of accepted extensions is cut off at the bottom. This is the first
thing a new user sees in a small window.

**Suggested fix.** Mostly a consequence of UI-008. Independently, give the drop zone a
`MinHeight` that fits its content, and use a more compact empty state (smaller icon, one line)
when space is tight.

---

### UI-011 — Title bar and toolbar use two rows of chrome

- **Status:** Done
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:38-111`

**Problem.** The title bar and the toolbar are separate rows and together take roughly 100px
before any content. With vertical space already short (UI-008), that is expensive.

**Suggested fix.** Optional: host the Obfuscate/Cancel buttons and the icon buttons inside the
`TitleBar` (WPF-UI supports header content), or reduce the toolbar's vertical padding.

---

## Settings panel

### UI-012 — Collapsed expanders do not show whether a technique is on

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:36-400`

**Problem.** Each technique's on/off switch is inside its expander, and only String Encryption
starts expanded. To see what a preset enabled, the user has to open up to twelve cards one by
one. Help says presets "fill the expanders so you can see what that preset turned on" — but
you cannot see it without opening them.

**Suggested fix.** Put the toggle in the `CardExpander` header (`HeaderContent`), or show an
On/Off badge there. The expander body then holds only the options. Consider a one-line summary
of the active techniques under the level dropdown.

**Done when.** With every expander collapsed, the user can tell which techniques are enabled
and can turn one on or off without expanding it.

---

### UI-013 — Obfuscation level dropdown text is truncated

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:20-25`,
  `Src/Obfy.Core/Models/ObfuscationLevel.cs:13-31`

**Problem.** The enum descriptions are long ("Standard — rename, encrypt strings, strip
metadata"). At the default 300px panel width the selected value is cut off after "encrypt
strings,", with no ellipsis and no tooltip.

**Suggested fix.** Show only the level name in the closed dropdown and put the explanation in a
secondary line below it (and in the dropdown items). That also frees the description text from
having to fit in one line.

---

### UI-014 — Disabled Obfuscate button gives no reason

- **Status:** Done
- **Priority:** P2 · **Evidence:** Code (disabled state observed; reasons read from code)
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:119-121`,
  `Src/Obfy.UI/Views/MainWindow.xaml:56-60`

**Problem.** Obfuscate is disabled when there are no included files, or when Watermark is on
and the id is empty. The only explanation is in `AutomationProperties.HelpText`, which sighted
users never see. If the Watermark expander is collapsed, the button is simply dead with no hint
why.

**Suggested fix.** Expose a `ObfuscateBlockedReason` string from the view model. Show it as a
tooltip on the button (`ToolTipService.ShowOnDisabled="True"`) and in the status bar. Mark the
Watermark id box as invalid when it is required and empty.

---

### UI-015 — Runtime profile does not affect the toggles it gates

- **Status:** Done
- **Priority:** P2 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:27-34,144-235`,
  `Src/Obfy.Core/Models/ObfySettings.cs:685-696`

**Problem.** With NativeAOT, Unity IL2CPP or Blazor WebAssembly selected, Method IL
Encryption, Anti-Dump, dependency embedding and Virtualization are gated off by Core. The
panel does not react to the profile: those toggles still look enabled and can be switched on,
so the user believes a protection is applied when it is not.

**Suggested fix.** Add per-technique "available for this profile" properties to
`SettingsViewModel`. Disable the gated toggles and show a short note ("Not available for
NativeAOT"). Keep the stored value so switching the profile back restores it.

---

### UI-016 — Options are grouped under the wrong expanders

- **Status:** Done
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:215-235,279-291`

**Problem.** "Packing" contains **Incremental cache** and **Virtualize methods**, neither of
which is packing. "Assembly Merge" contains **Embed referenced DLLs**, which is a separate
technique that works without merging.

**Suggested fix.** Move Virtualize methods to Protection (or its own card), move Incremental
cache to a "Build" or "General" group near the top, and give dependency embedding its own card
or rename the group to "Merge & embedding".

---

### UI-017 — Protection toggles have no descriptions

- **Status:** Done
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:144-187`

**Problem.** Anti-Debug, Anti-Tamper, Anti-Dump, Anti-Decompiler, Reference Proxy and Method
IL Encryption are bare switches. Only three toggles in the whole panel have tooltips (Packing,
Incremental, Virtualization). Several of these have real limits the user should know before
enabling them (Windows-only, x86/x64 only, in-process only).

**Suggested fix.** Add a one-line secondary description under each toggle, taken from the
technique table in `CLAUDE.md` / `docs/Techniques.md`, plus a tooltip for the caveats.

---

### UI-018 — Signing key file has no Browse button

- **Status:** Done
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:245-249`

**Problem.** The key file is a free-text box; the user has to type or paste a full path. There
is no validation that the file exists or has a supported extension.

**Suggested fix.** Add a Browse button backed by a new `IFileDialogService` method filtered to
`.snk`/`.pfx`/`.p12`, and flag a path that does not exist.

---

### UI-019 — Some settings have no UI at all

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/SettingsViewModel.cs:80,106-107,213-215`,
  `Src/Obfy.UI/ViewModels/FilesViewModel.cs:37-38`

**Problem.** These are carried through load/save but cannot be viewed or edited in the app:

- **Inclusions** (namespaces, types, methods) — the collections exist in the view model
- **Packing RID** (`win-x64` vs `portable`) — a tooltip tells the user to "set packing.rid to
  portable" but there is nowhere to do it
- **Dependency embedding include/exclude patterns**
- **Symbol map path**

A user who loads a config with inclusions has no way to see that they are active.

**Suggested fix.** Add an Inclusions section mirroring Exclusions, a RID dropdown under
Packing, and pattern lists under dependency embedding. At minimum, show a read-only note when
a loaded config contains values the panel does not display.

---

### UI-020 — Label casing and wording are inconsistent

- **Status:** Done
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml`, `FilesPanel.xaml`,
  `MainWindow.xaml`

**Problem.**

- Mixed casing: "Obfuscation level" / "Runtime profile" vs "Output Directory" / "Input Files";
  "Rename Types" / "Preserve Public API" vs "Check entry point" / "Inject junk types".
- Each toggle repeats its expander title in a different case ("String Encryption" →
  "String encryption").
- The exclusion lists use three near-identical hand-copied blocks.

**Suggested fix.** Pick sentence case throughout (the Fluent convention). Once UI-012 moves the
toggles into the headers the duplicate labels disappear. Extract the exclusion list into one
reusable template or control.

---

### UI-021 — Choosing a preset silently overwrites Custom settings

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/SettingsViewModel.cs:272-278,285-365`

**Problem.** Selecting Minimal, Standard or Aggressive while on Custom immediately replaces
the user's hand-tuned toggles. There is no confirmation and no undo. It is easy to do by
accident with the keyboard (arrow keys on the focused dropdown).

**Suggested fix.** Keep the last Custom state in memory and restore it when the user selects
Custom again, or show an "Undo" snackbar after a preset replaces a Custom configuration.

---

## Files, progress and toolbar

### UI-022 — Progress bar sits at 0% for closed-set and merge runs

- **Status:** Done
- **Priority:** P2 · **Evidence:** Code (the run in the review finished too fast to observe)
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:364,512` (jump to 100),
  `:271,298` (per-file steps), `Src/Obfy.UI/Views/MainWindow.xaml:73-79`

**Problem.** The toolbar shows a determinate bar and a percentage. For closed-set and merge
runs `OverallProgress` is only set once, to 100, after the whole run returns — so on a large
solution the bar shows 0% for the entire duration and looks hung. Per-file bars likewise only
ever show 0 or 100. Core's services expose no progress reporting.

**Suggested fix.** Short term: make the bar indeterminate (and hide the percentage) for
closed-set and merge runs, and for single files while they process. Longer term: add
`IProgress<T>` to `IObfuscationService` reporting the current obfuscator and module, and show
the stage name next to the bar.

---

### UI-023 — File status icons are monochrome and rows hide the output path

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:84-108`

**Problem.** The status icon always uses the primary text colour, so pending, success, error
and skipped rows differ only by a small glyph shape. After a successful run the row still
shows only the input path; `OutputPath` is set on the model but never displayed, and there is
no way to open or reveal one file's output.

**Suggested fix.** Colour the icon by status (success / critical / caution brushes, as the log
already does) while keeping the distinct glyphs so colour is not the only signal. Show the
output path on successful rows and add a row action or context menu: Open output, Reveal in
Explorer, Copy path. Add a tooltip with the full path for trimmed text.

---

### UI-024 — Open output folder is always enabled and its icon is ambiguous

- **Status:** Done
- **Priority:** P2 · **Evidence:** Code (icons observed)
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:840-859`,
  `Src/Obfy.UI/Views/MainWindow.xaml:84-97`,
  `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:145-150`

**Problem.**

- The command has no `CanExecute`. Before any run it is enabled and clicking it does nothing —
  no message, no log line.
- It opens the folder of the last file's output rather than the configured Output Directory,
  so it is unavailable until a run has succeeded.
- The toolbar has three folder-like icons with no labels: Open output folder
  (`FolderArrowRight24`), Load Configuration (`FolderOpen24`) and, a few centimetres away,
  Browse output directory (also `FolderOpen24`).

**Suggested fix.** Add a `CanExecute` (enabled when a last output exists or Output Directory is
set and exists), fall back to the Output Directory, and notify when the folder is missing. Use
a distinct icon for Load Configuration (for example a document/settings glyph), or add text
labels to the config buttons.

---

### UI-025 — File list has no keyboard or bulk actions

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:62-122`,
  `Src/Obfy.UI/ViewModels/FilesViewModel.cs:133-146`

**Problem.** Rows are selectable but selection does nothing. There is no Delete key, no
multi-select removal and no context menu — only a per-row × button and "Clear" for everything.
With a large solution, removing a handful of projects is slow. Every × button also has the
same accessible name, "Remove file", so a screen-reader user cannot tell them apart. Skipped
rows cannot be re-included, and included rows cannot be excluded without removing them.

**Suggested fix.** Enable extended selection, bind Delete to remove the selection, add a
context menu, and include the file name in each remove button's automation name. Consider a
checkbox per row to include/exclude without removing.

---

### UI-026 — Files can only be dropped on the Files panel

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:10`

**Problem.** `AllowDrop` is set only on the Files panel. Dropping a file on the Settings panel,
the log or the toolbar is rejected, although the intent is unambiguous.

**Suggested fix.** Handle drag-and-drop at the window level and show the overlay over the Files
panel (or the whole window). Dropping a `.json` config could load it.

---

### UI-027 — Typed output directory may be ignored on Ctrl+Enter

- **Status:** Done
- **Priority:** P3 · **Evidence:** Unconfirmed
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:140-144`,
  `Src/Obfy.UI/Views/MainWindow.xaml:23`

**Problem.** The Output Directory box binds with the default `LostFocus` trigger. Ctrl+Enter
is a window-level key binding that does not move focus, so typing a path and pressing
Ctrl+Enter should start the run with the *previous* value. Not reproduced yet.

**Suggested fix.** Reproduce first. If confirmed, use `UpdateSourceTrigger=PropertyChanged`
(with the preference save debounced), or flush the focused binding before running the command.

---

### UI-028 — Drop overlay may flicker while dragging

- **Status:** Done
- **Priority:** P3 · **Evidence:** Unconfirmed
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml.cs:34`,
  `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:43-50`

**Problem.** `OnDragLeave` hides the overlay on every `DragLeave`, which WPF also raises when
the pointer moves between child elements — including onto the overlay itself when it appears.
That pattern usually makes the overlay flicker. Not reproduced yet (the review did not drag by
hand).

**Suggested fix.** Reproduce first. If confirmed, make the overlay `IsHitTestVisible="False"`
and only hide it when the pointer has actually left the control's bounds.

---

## Results and output log

### UI-029 — Copy is enabled with nothing selected

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed (button enabled with no selection; cause read from code)
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:58-60,299-314`

**Problem.** `SelectedNode` is annotated to refresh `CopySymbolCommand`, but the command has no
`CanExecute`, so the Copy button is always enabled. With no selection it does nothing and gives
no feedback. A successful copy gives no feedback either.

**Suggested fix.** Add `CanExecute` (a node is selected) and a brief confirmation on success.
Consider "Copy original" / "Copy obfuscated" in the context menu, and Ctrl+C on the tree.

---

### UI-030 — Statistics strip does not add up to the total

- **Status:** Done
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:52-86`,
  `Src/Obfy.Core/Models/ObfuscationResult.cs:120-180`

**Problem.** The strip shows Strings, Types, Methods, Fields, Control Flow and Total. In the
review run those were 24, 4, 6, 8, 0 and **96** — the visible parts sum to 42. The rest comes
from counters that are never shown: properties, parameters, events, namespaces, protections
applied, methods encrypted, metadata items removed, and others. The total looks wrong.

**Suggested fix.** Generate the strip from the statistics object and show every non-zero
counter, grouped (Renamed: types/methods/fields/…; Encrypted: strings/constants/resources;
Protection; Metadata). Hide zero counters rather than showing "Control Flow: 0" when the
technique was off.

---

### UI-031 — Symbol search rebuilds the whole tree on every keystroke

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:158-234,316`

**Problem.** `OnSearchTextChanged` calls `ApplyFilter` synchronously, which re-parses every key
(twice) and rebuilds the `ObservableCollection` on the UI thread for each character typed.
Fine for a small library; for an assembly with tens of thousands of symbols typing will
stutter. Not measured.

**Suggested fix.** Debounce the search (about 200ms), parse each key once when results load and
filter the parsed list, and replace the collection in one operation instead of item by item.
Measure with a large symbol map before and after.

---

### UI-032 — Preview shows only the last output

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:177-182,655-681`,
  `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:150-173`

**Problem.** With several assemblies the Preview tab decompiles only the last successful output
and there is no way to choose another. The tab does not say which file it is showing. The text
is a plain read-only box with no syntax colouring.

**Suggested fix.** Add a file selector to the Preview tab (or preview the row selected in the
file list), label the previewed file, and decompile on demand rather than at the end of every
run.

---

### UI-033 — Log is cleared at the start of every run

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/MainViewModel.cs:146`

**Problem.** `Output.Clear()` runs at the start of each obfuscation, so the previous run's
warnings and errors are gone the moment the user retries. Comparing two runs is impossible.

**Suggested fix.** Keep the log and insert a separator line per run, or make "clear on run" a
toggle. The Clear button already covers manual clearing.

---

### UI-034 — Log lines cannot be copied individually and never wrap

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code (horizontal scrollbar observed)
- **Where:** `Src/Obfy.UI/Views/Controls/OutputPanel.xaml:47-102`

**Problem.** The log is a `ListBox` of non-selectable text: Ctrl+C on a selected line does
nothing, and the only copy action copies the whole log. Messages are `NoWrap`, so long error
messages and paths need horizontal scrolling. The two toggle switches plus two buttons take up
most of the header.

**Suggested fix.** Support Ctrl+C / context-menu copy for the selected lines (extended
selection), add a "Wrap" option, and move Timestamps / Auto-scroll / Wrap into a small overflow
menu. A level filter (errors and warnings only) would help on long runs.

---

## Cleanup

### UI-035 — Dead code and unused dependencies

- **Status:** Done
- **Priority:** P3 · **Evidence:** Code
- **Where:** see list

- [x] `LevelToColorConverter` is never used; the log uses `DataTrigger`s instead. Remove the
      class and its registration — `Src/Obfy.UI/Converters/LevelToColorConverter.cs`,
      `Src/Obfy.UI/App.xaml:22`
- [x] `AddSourceFilesCommand` and `ShowOpenSourceDialog` are not bound to any view; Add Files
      already accepts `.cs`. Only a unit test calls the command
      (`Tests/Obfy.UI.Tests/ViewModels/FilesViewModelTests.cs:514`), so remove that test with
      it — `Src/Obfy.UI/ViewModels/FilesViewModel.cs:126-131`,
      `Src/Obfy.UI/Services/FileDialogService.cs:22`,
      `Src/Obfy.UI/Services/IFileDialogService.cs:18`
- [x] `SymbolTreeNode.IsExpanded` is never bound to `TreeViewItem.IsExpanded`. Either bind it
      (useful for auto-expanding search matches once UI-001 is fixed) or remove it —
      `Src/Obfy.UI/Models/SymbolTreeNode.cs:34`
- [x] `Microsoft.Xaml.Behaviors.Wpf` is referenced but not used in any XAML or code —
      `Src/Obfy.UI/Obfy.UI.csproj:22`
- [x] `InternalsVisibleTo Include="Obfy.UI.Tests"` appears twice —
      `Src/Obfy.UI/Obfy.UI.csproj:15,39`
- [x] `SymbolTreeNode.HasChildren` and the `LogEntry.Info`/`Warning`/`Error`/`Success`/`Debug`
      static factories have no callers in `Src` or `Tests` (only `LogEntry.Create` is used) —
      `Src/Obfy.UI/Models/SymbolTreeNode.cs`, `Src/Obfy.UI/Models/LogEntry.cs`
