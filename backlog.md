# UI Backlog

Findings from two full reviews of the WPF desktop app (`Src/Obfy.UI`).

- **First review** (commit `7805545`): UI-001 to UI-035. All were addressed on `fix/ui-backlog`.
- **Second review** (commit `912ecf6`, after that fix): 31 of the 35 hold up. Four are
  **reopened** (UI-006, UI-010, UI-024, UI-034) and 24 new items were found (UI-036 to
  UI-059), several of them regressions from the rework. See
  [Second review](#second-review-2026-11-20-commit-912ecf6) at the end.

The sections for UI-001 to UI-035 keep the original problem statements.

- **Reviewed:** 2026-11-20, commit `7805545` (v2.0.0), Debug build
- **Fixed:** `fix/ui-backlog`, merged to `main`. Product version stays 2.0.0.
- **Re-reviewed:** 2026-11-20, commit `912ecf6`, Debug build, same method as below with
  isolated preferences (`OBFY_UI_PREFERENCES`). `Tests/Obfy.UI.Tests` passed (190).
  `Tests/Obfy.UI.AutomationTests` smoke tests: 3 passed, 2 failed (UI-038).
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

- **Status** — Done, Reopened (marked Done but the second review found it not met), or Open. After the hybrid rewrite also: Closed (rewrite), Fixed (hybrid), Partly fixed (hybrid), Re-verify (hybrid) — see [Hybrid UI rewrite](#hybrid-ui-rewrite)
- **Priority** — P1 (wrong or broken behaviour a user will hit), P2 (clear usability cost),
  P3 (polish or cleanup)
- **Evidence** — *Observed* (seen in the running app), *Code* (follows from reading the code,
  not exercised), or *Unconfirmed* (suspected; reproduced from the code before fixing)
- **Where** — paths are relative to the repo root, as of the review commit

UI-011 was fixed by shrinking the toolbar, not by moving buttons into the title bar.
UI-022 uses an indeterminate bar. It does not add pipeline progress.

## Hybrid UI rewrite

The WPF screen these findings describe was replaced by a WPF shell hosting WebView2 and a React page
(design and plan: `docs/superpowers/specs/2026-11-20-hybrid-ui-redesign-design.md` and `docs/superpowers/plans/2026-11-20-hybrid-ui-redesign.md`). Paths in the items below point at the
removed WPF code. What happened to the second-review items (UI-036 to UI-059):

- **Closed (rewrite):** UI-036, 037, 038, 041, 042, 043, 046, 047, 048, 049, 051, 057. They described WPF
  layout or controls that no longer exist. UI-036 is moot: the log is now a tab on the Results screen.
- **Fixed (hybrid):** the logic defects were fixed while the rules were lifted into the WPF-free
  `Obfy.App` sessions, each with a regression test: UI-039, 040, 055 (`ProtectionSession`, plan Task 9);
  UI-053, 054, 058 (`InputSession`, Task 10); UI-044, 050, 056, 059 (`ResultsStore`, Task 12);
  UI-052, 053 (`RunCoordinator`, Task 13).
- **Partly fixed:** UI-045. The path box is only live while the symbol map is on and a relative path
  resolves against the output folder. The Browse button is a tracked follow-up: it needs an
  `output.pickSymbolMap` endpoint (host save dialog), which has not been added.
- **Re-verify (hybrid):** UI-006, 010, 024 and 034 (reopened in the second review). The code they point at
  is gone and the new screens address them, but nobody has re-checked them by hand.
- UI-001 to UI-035 were fixed in the WPF app. Where a screen was rebuilt, those fixes were not carried over
  by code; the behaviour that mattered (symbol tree, closed-set rules, output defaults, level restore) is in
  the sessions and covered by `Obfy.App.Tests`. The new page has not had a fresh manual review (light theme,
  high contrast, screen reader), and the first review's "not covered" list still applies.

## Index

| ID | Priority | Status | Area | Title |
|----|----------|--------|------|-------|
| UI-001 | P1 | Done | Results | Symbol tree never groups members under their type |
| UI-002 | P1 | Done | Output | Log timestamp and level columns collide |
| UI-003 | P1 | Done | Accessibility | Status bar message is hidden from screen readers |
| UI-004 | P1 | Done | Files | Output directory preference is polluted and never validated |
| UI-005 | P1 | Done | Help | Help dialog clips at the minimum window size |
| UI-006 | P1 | Re-verify (hybrid) | Help | Help text is out of date |
| UI-007 | P1 | Done | Files | Output directory placeholder is wrong for closed-set runs |
| UI-008 | P1 | Done | Layout | Files, Results and Output do not fit in one column |
| UI-009 | P2 | Done | Layout | Results panel cannot be dismissed |
| UI-010 | P2 | Re-verify (hybrid) | Layout | Empty-state drop zone is clipped at the minimum size |
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
| UI-024 | P2 | Re-verify (hybrid) | Toolbar | Open output folder is always enabled and its icon is ambiguous |
| UI-025 | P3 | Done | Files | File list has no keyboard or bulk actions |
| UI-026 | P3 | Done | Files | Files can only be dropped on the Files panel |
| UI-027 | P3 | Done | Files | Typed output directory may be ignored on Ctrl+Enter |
| UI-028 | P3 | Done | Files | Drop overlay may flicker while dragging |
| UI-029 | P2 | Done | Results | Copy is enabled with nothing selected |
| UI-030 | P2 | Done | Results | Statistics strip does not add up to the total |
| UI-031 | P3 | Done | Results | Symbol search rebuilds the whole tree on every keystroke |
| UI-032 | P3 | Done | Results | Preview shows only the last output |
| UI-033 | P3 | Done | Output | Log is cleared at the start of every run |
| UI-034 | P3 | Re-verify (hybrid) | Output | Log lines cannot be copied individually and never wrap |
| UI-035 | P3 | Done | Cleanup | Dead code and unused dependencies |
| UI-036 | P1 | Closed (rewrite) | Output | Log options menu never opens |
| UI-037 | P1 | Closed (rewrite) | Settings | Protection toggle labels overlap their card titles |
| UI-038 | P1 | Closed (rewrite) | Tests | Two UI smoke tests fail |
| UI-039 | P2 | Fixed (hybrid) | Settings | Settings outside the presets switch the level to Custom |
| UI-040 | P2 | Fixed (hybrid) | Settings | Turning a technique on does not reveal its required options |
| UI-041 | P2 | Closed (rewrite) | Settings | Cards are tall, titles are cut off and toggles are misaligned |
| UI-042 | P2 | Closed (rewrite) | Accessibility | Expander cards have no accessible name |
| UI-043 | P2 | Closed (rewrite) | Layout | Output directory box has no visible label |
| UI-044 | P2 | Fixed (hybrid) | Results | Preview decompiles on the UI thread |
| UI-045 | P2 | Partly fixed (hybrid) | Files | Symbol map path is always editable and relative paths go to the working directory |
| UI-046 | P3 | Closed (rewrite) | Settings | Cards expand to an empty body |
| UI-047 | P3 | Closed (rewrite) | Visual | Disabled buttons look emphasised |
| UI-048 | P3 | Closed (rewrite) | Files | File list scrolls sideways instead of trimming paths |
| UI-049 | P3 | Closed (rewrite) | Results | Statistics strip labels are malformed |
| UI-050 | P3 | Fixed (hybrid) | Results | Unrenamed types show as a rename to themselves |
| UI-051 | P3 | Closed (rewrite) | Layout | Tabs repeat their own heading and Preview shows the path twice |
| UI-052 | P3 | Fixed (hybrid) | Commands | Commands are enabled with nothing to act on |
| UI-053 | P3 | Fixed (hybrid) | Files | Merge output name and symbol map folder ignore the include checkbox |
| UI-054 | P3 | Fixed (hybrid) | Files | Re-including a skipped solution row loses its reason |
| UI-055 | P3 | Fixed (hybrid) | Settings | Choosing Custom again discards exclusions added on a preset |
| UI-056 | P3 | Fixed (hybrid) | Results | Symbol parsing and search run on the UI thread and scale badly |
| UI-057 | P3 | Closed (rewrite) | Layout | No splitter between the file list and the tabs |
| UI-058 | P3 | Fixed (hybrid) | Files | Drop overlay omits .json and appears during a run |
| UI-059 | P3 | Fixed (hybrid) | Results | Results tab has no before-run state |

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

- **Status:** Re-verify (hybrid)
- **Hybrid UI:** The Help dialog is gone; the Guide screen and technique copy (`Src/WebApp/src/content/*.json`) are new, with a contract test for the technique ids. The text has not been re-reviewed against the product.
- **Reopened (second review, `912ecf6`, Observed):** the content now covers solutions, closed
  sets and skipped rows, but the "Getting started" output paragraph is assembled from
  placeholder constants and reads wrong: "one file is written Next to the input as
  \*.obfuscated.\* A closed set … Merge writes Next to the first input as \*.obfuscated.\*" —
  capital "Next" mid-sentence and missing full stops (`Src/Obfy.UI/Help/HelpCatalog.cs:27`).
  Help also says "Output goes to Output Directory", but that box no longer has a visible label
  (UI-043). The Techniques topic still uses the old names and wording: "Assembly Merge" (the
  card is now "Merge and embedding"), "Set packing.rid to portable" (there is now a dropdown),
  and Virtualize methods is not listed under Protection (`HelpCatalog.cs:48-60`). Write the
  sentences out in full rather than splicing placeholder strings into prose.
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

- **Status:** Re-verify (hybrid)
- **Hybrid UI:** The WPF empty-state drop zone is gone; the Files screen has a new empty state (`EmptyState.tsx`). Not re-measured at 900x600.
- **Reopened (second review, `912ecf6`, Observed):** still clipped at 900×600 before a run. The
  icon is cut off at the top and the extensions line at the bottom. The file list row is `*`
  against a `2*` tab area (`Src/Obfy.UI/Views/MainWindow.xaml:155-159`), which leaves the drop
  zone about 70px, and it has no `MinHeight`
  (`Src/Obfy.UI/Views/Controls/FilesPanel.xaml:55-62`). Give the files row a `MinHeight` that
  fits the empty state, or use a one-line empty state when the row is short.
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

- **Status:** Re-verify (hybrid)
- **Hybrid UI:** The WPF toolbar is gone; `files.openOutput` and the run bar replace it. Whether the action is enabled only when an output exists has not been re-checked.
- **Reopened (second review, `912ecf6`, Observed):** the command now has a `CanExecute` and a
  distinct Load icon, but nothing re-evaluates it when the output directory changes. With an
  empty box and no run, typing (or browsing to) an existing folder leaves the button disabled
  with "No output folder yet." `MainViewModel` refreshes the command only on file-list and
  settings changes (`Src/Obfy.UI/ViewModels/MainViewModel.cs:108-118,206-215`); it does not
  listen for `Files.OutputDirectory`. Subscribe to `Files.PropertyChanged` for
  `OutputDirectory` and call `RefreshFileCommands`, with a regression test.
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

- **Status:** Re-verify (hybrid)
- **Hybrid UI:** The WPF output log is gone; the Results Log tab is new (`LogTab.tsx`, `LogLines.tsx`). Per-line copy and wrapping have not been re-checked.
- **Reopened (second review, `912ecf6`):** copying selected lines is in place (Ctrl+C and a
  context menu; read from code, not exercised). Wrapping is not:
  - *Observed:* the Wrap option cannot be reached at all, because the options menu never opens
    (UI-036).
  - *Code:* even when reachable it cannot wrap. Each log row is a horizontal `StackPanel`
    (`Src/Obfy.UI/Views/Controls/OutputPanel.xaml:96`), which measures its children with
    unlimited width, so `TextWrapping="Wrap"` has no effect; with the horizontal scrollbar
    disabled the text would be clipped instead.
  - *Observed:* the same template no longer aligns the message column — "Success" rows start
    further right than "Info" rows.

  Use a `Grid` per row with `Auto` columns for timestamp and level (`SharedSizeGroup` to keep
  them aligned) and a `*` column for the message.
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

---

## Second review (2026-11-20, commit `912ecf6`)

A fresh review of the app after `fix/ui-backlog` was merged. Same method as the first review,
with preferences redirected through `OBFY_UI_PREFERENCES`. Paths and line numbers below are as
of `912ecf6`.

**Confirmed working:** nested symbol tree, statistics that add up (96 = 96), the
Output / Results / Preview tabs with Results selected after a successful run, separated log
timestamp and level, the status message exposed to UI Automation, Help fitting at 900×600, the
missing-folder warning with isolated preferences, and runtime-profile gating of protections.

**Reopened:** UI-006, UI-010, UI-024 and UI-034 — see the note at the top of each.

**Suggested order:** UI-036, UI-037, UI-040 and the UI-024 reopen first. Those four make the
smoke tests pass again (UI-038), which then guard the rest. After that UI-039, UI-041 and
UI-042.

### UI-036 — Log options menu never opens

- **Status:** Closed (rewrite)
- **Hybrid UI:** Moot. The WPF log options menu is gone; the Results screen has a Log tab (`features/results/LogTab.tsx`).
- **Priority:** P1 · **Evidence:** Observed (not clicked by hand — see below)
- **Where:** `Src/Obfy.UI/Views/Controls/OutputPanel.xaml:29-50`,
  `Tests/Obfy.UI.AutomationTests/TestBase.cs:41-59`

**Problem.** Timestamps, Auto-scroll, Wrap and Problems only were moved into a
`ui:DropDownButton` whose `Flyout` is a `ui:Flyout`. WPF-UI 4.3.0's `DropDownButton` only
opens a `ContextMenu`: reflecting over the shipped `Wpf.Ui.dll` shows a single `_contextMenu`
field, `OnFlyoutChanged` casting the value to `ContextMenu`, and the open logic living in
`OnPreviewMouseLeftButtonUp`. Any other flyout type is ignored.

Invoking the button through UI Automation opened no popup and none of the four toggles could
be found, which is also why `Smoke_WindowChrome_IsPresentAndObfuscateDisabled` fails. The
button was not clicked with a physical mouse during the review; the reflection result is the
basis for saying a click does nothing either.

All four log options are therefore unreachable, including the two that existed before the
rework.

**Suggested fix.** Use a `ContextMenu` with checkable `MenuItem`s bound to the four
properties, or a `ToggleButton` with a `Popup`. Because `DropDownButton` opens on mouse-up
only, check that the menu also opens from the keyboard (Enter/Space) and through UI
Automation; if it does not, prefer the `ToggleButton` + `Popup`.

**Done when.** Each option can be toggled by mouse and keyboard, and the smoke test that looks
for the toggles passes.

---

### UI-037 — Protection toggle labels overlap their card titles

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF protection cards are gone. The Protection screen is a technique list with a detail pane.
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:226-229,247-249,262-267,297-299,320-322,335-340`

**Problem.** Six header toggles — Anti-debug, Anti-tamper, Anti-dump, Anti-decompiler,
Reference proxy and Method IL encryption — still have a `Content` label. The toggle is layered
over the card header with only 72px reserved for it, so the label is drawn on top of the
title: "Anti-decompiler" over "Anti-decompiler", "Method IL encryption" over "Method IL
encrypt", and so on. The text is unreadable. The other header toggles have no `Content` and
are fine.

**Suggested fix.** Remove `Content` from these six and give them
`AutomationProperties.Name="Enable …"` like the others. UI-041 covers the layering approach
itself.

**Done when.** No header toggle draws text over its card title at any panel width.

---

### UI-038 — Two UI smoke tests fail

- **Status:** Closed (rewrite)
- **Hybrid UI:** `SmokeTests.cs` and the controls it located are gone. `Tests/Obfy.UI.AutomationTests` now has one FlaUI window smoke and five Playwright-over-WebView2 end-to-end tests (`Category=UI`).
- **Priority:** P1 · **Evidence:** Observed
- **Where:** `Tests/Obfy.UI.AutomationTests/SmokeTests.cs:38,104`

**Problem.** `dotnet test Tests/Obfy.UI.AutomationTests --filter FullyQualifiedName~SmokeTests`
on `912ecf6`: 3 passed, 2 failed.

- `Smoke_WindowChrome_IsPresentAndObfuscateDisabled` — `FindLogOption("AutoScrollToggle")`
  is null. Caused by UI-036.
- `Smoke_ProtectionAndMerge_AreReachable` — `FindById("WatermarkIdBox")` is null. Caused by
  UI-040.

The fix commit recorded that FlaUI `Category=UI` was not run. At review time the CI run for
`912ecf6` had not finished; CI had concluded `failure` on the two commits before it (not
investigated here).

**Suggested fix.** Fix UI-036 and UI-040, then run the full `Category=UI` set locally before
merging UI changes. Find out why CI was already red on `7805545` and `60f5caa`.

**Done when.** `Category=UI` passes locally and the Windows CI job is green.

---

### UI-039 — Settings outside the presets switch the level to Custom

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ProtectionSession` (plan Task 9), with a regression test in `Tests/Obfy.App.Tests/Sessions/ProtectionSessionTests.cs`.
- **Priority:** P2 · **Evidence:** Observed (runtime profile); Code (the others)
- **Where:** `Src/Obfy.UI/ViewModels/SettingsViewModel.cs:446-466`,
  `Src/Obfy.UI/ViewModels/SettingsViewModel.Extras.cs:15-40`

**Problem.** Choosing NativeAOT as the runtime profile changed the level from Standard to
Custom. Every property change that is not in `PresetNeutralProperties` does this, and that
list only holds draft text and derived values. So settings no preset touches — runtime
profile, watermark and its id, signing and its key file, packing RID, merge, dependency
embedding, Preserve public API / XAML — all turn "Standard" into "Custom", although the
techniques still match Standard.

**Suggested fix.** Switch to Custom only when a property that `ApplyPreset` assigns changes.
Derive that set from one list used by both `ApplyPreset` and the change handler so they cannot
drift.

**Done when.** Changing the runtime profile, watermark, signing or packing settings on a
preset leaves the level unchanged; changing a technique a preset sets still switches to
Custom. Covered by unit tests.

---

### UI-040 — Turning a technique on does not reveal its required options

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ProtectionSession` (plan Task 9): the page shows a technique's required options with the technique; regression test in `ProtectionSessionTests`.
- **Priority:** P2 · **Evidence:** Observed (failing smoke test); status text from code
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:375-402`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:121-133`

**Problem.** The header toggle turns Watermark on without expanding the card. Obfuscate goes
disabled and the status bar says "Enter a watermark id.", but the id box is inside the
collapsed card — it is not on screen and not in the automation tree. The user has to guess
that the fix is behind the chevron. Signing has the same shape: the key file is hidden until
the card is expanded.

**Suggested fix.** Expand a card when its technique is switched on and it has required input
(Watermark, Signing), or expand any card with options when it is enabled. Clicking the status
message could also scroll to and focus the field.

**Done when.** Switching Watermark on shows the id box, focused, without a second click.

---

### UI-041 — Cards are tall, titles are cut off and toggles are misaligned

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF cards are gone; the Protection screen lays out a list and a detail pane.
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:57-521`

**Problem.** Each card layers a `ToggleSwitch` over a `CardExpander` in a `Grid`, with a
hard-coded margin (`0,14,36,0`) and a 72px gap reserved in the header. At the default 300px
panel width:

- Titles are cut off with no ellipsis: "Constant encryptic", "Strong-name signi", "Resource
  encryptic". In "Merge and embedding" the toggle labels are cut too: "Merge input assembli",
  "Embed referenced DL".
- Descriptions wrap to three to eight lines, so cards are roughly 120–240px tall. About five
  of the twenty fit on screen at 1400×900, where the old layout showed twice that. Seeing what
  is enabled still means a lot of scrolling — the point of UI-012.
- Toggles sit above the chevron rather than on its line. The Incremental cache toggle is
  centred on the whole card, so it moves down when the card expands.

**Suggested fix.** Build the header as one `Grid` (title and description in a `*` column, the
toggle in an `Auto` column) inside `CardExpander.Header` instead of overlaying. Keep the
description to one short line and move caveats to the tooltip or the expanded body. Trim long
titles with an ellipsis and a tooltip.

**Done when.** At 300px no title or label is cut off, every toggle lines up with its chevron
in both states, and at least ten cards are visible at 1400×900.

---

### UI-042 — Expander cards have no accessible name

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF expander cards are gone. The Protection screen is new markup with named switches. Accessibility on the new page (keyboard, names) has not been re-reviewed by hand.
- **Priority:** P2 · **Evidence:** Observed (UI Automation tree)
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:57-545,50-55`

**Problem.** With the header replaced by custom content, 18 of the 20 cards are exposed as a
group with an empty name containing a button (`ExpanderToggleButton`) with an empty name. Only
Inclusions and Exclusions, which still use a string header, are named. A screen-reader user
hears "button" twenty times.

Separately, the runtime profile list items are exposed as the raw enum names — `NativeAot`,
`UnityIl2Cpp`, `BlazorWasm` — rather than the descriptions shown on screen.

**Suggested fix.** Set `AutomationProperties.Name` on each `CardExpander`. For the combo
boxes, set the item container's automation name through `EnumDescriptionConverter`.

**Done when.** Every card and every combo item reports the text a sighted user sees.

---

### UI-043 — Output directory box has no visible label

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF output row is gone. The Files screen has an output card with a visible label.
- **Priority:** P2 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:173-178`

**Problem.** The "Output Directory" label was dropped when the row was compacted. The box now
shows only a placeholder such as "Next to the input as \*.obfuscated.\*", and once it holds a
path nothing says what the path is for. Help still calls it "Output Directory".

**Suggested fix.** Add a short label in front of the box ("Output folder"), or use the
labelled form of the text box. Keep the placeholder as the hint.

---

### UI-044 — Preview decompiles on the UI thread

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ResultsStore.LoadPreviewAsync` decompiles in `Task.Run` (plan Task 12); the test asserts the decompiler does not run on the calling thread.
- **Priority:** P2 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:166-217`

**Problem.** Selecting an assembly in the Preview tab calls `LoadPreview` straight from the
property-changed handler, which runs `AssemblyPreview.Decompile` synchronously. Before the
rework the decompile ran in `Task.Run`. A large assembly will freeze the window for the whole
decompile, with no progress indication and no way to cancel. Not measured — the review used
two small libraries.

**Suggested fix.** Decompile on a background thread, show a progress ring, cancel the previous
request when the selection changes, and ignore results that arrive for a stale selection.

**Done when.** The window stays responsive while a large assembly is decompiled, and switching
selection mid-decompile shows the newly selected file.

---

### UI-045 — Symbol map path is always editable and relative paths go to the working directory

- **Status:** Partly fixed (hybrid)
- **Hybrid UI:** `InputSession` (plan Task 10): the symbol-map path box is shown only while "Write symbol map" is on, `output.setSymbolMap` refuses a path while it is off, and a relative path resolves against the output folder that will be used (`SymbolMapPath_IsEditableOnlyWhileTheOptionIsOn_AndResolvesAgainstTheOutputFolder`). **Not done:** the Browse button. It needs a new `output.pickSymbolMap` endpoint (a save-file dialog on the host, like `output.pickFolder`), which is a tracked follow-up and has not been added. The path is still not saved between sessions.
- **Priority:** P2 · **Evidence:** Observed (always editable); Code (path resolution)
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:203-216`,
  `Src/Obfy.UI/ViewModels/FilesViewModel.cs:476-488`

**Problem.** The symbol map path box is enabled while "Write symbol map" is off, so it looks
live when it is ignored. When it is used, `ResolveSymbolMapPath` returns the text as typed.
The placeholder shows `symbolmap.json`, which invites a bare file name — and a relative path
resolves against the process working directory, not the output folder. Started from the Start
menu or an installer shortcut, that can be the install folder or `System32`. There is no
Browse button, and the path is not saved between sessions.

**Suggested fix.** Enable the box only when the toggle is on. Resolve a relative path against
the output folder that will actually be used. Add a Browse button and show the resolved
location as the placeholder.

---

### UI-046 — Cards expand to an empty body

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF expander cards are gone.
- **Priority:** P3 · **Evidence:** Observed (Incremental cache, Constant encryption)
- **Where:** `Src/Obfy.UI/Views/Controls/SettingsPanel.xaml:58-68,87-88,217-225,253-261,326-334,344-352,362-373`

**Problem.** Five cards have a chevron but no body at all: Incremental cache, Anti-debug,
Anti-dump, Method IL encryption and Virtualize methods. Metadata keeps its checkboxes in the
header, so its body is empty too. For the rest, the body is hidden while the technique is off,
so expanding an off card also shows an empty strip.

**Suggested fix.** Use a plain card (no chevron) for techniques without options. For
techniques that are off, show the options disabled instead of hiding them, so the user can see
what turning it on would offer.

---

### UI-047 — Disabled buttons look emphasised

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF button styles are gone; the page has its own button styles.
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/App.xaml:13-17`

**Problem.** The app overrides the disabled button brushes so a disabled Obfuscate does not
vanish on dark. The border uses `ControlStrongStrokeColorDefault`, which is brighter than an
enabled button's border. Disabled buttons — Copy in Results, Open output folder, Obfuscate —
end up as the most prominent buttons in their row and read as focused or selected.

**Suggested fix.** Keep the readable disabled foreground but use the normal or a weaker stroke
for the border, and lower the content opacity slightly.

---

### UI-048 — File list scrolls sideways instead of trimming paths

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF file list is gone; the Files screen has a new table (`FileTable.tsx`).
- **Priority:** P3 · **Evidence:** Observed (900×600)
- **Where:** `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:64-73,124-130`

**Problem.** The file list allows horizontal scrolling, so rows are as wide as their longest
path and `TextTrimming` never applies. At the minimum window size a horizontal scrollbar
appears across the list and covers the last line of the only visible row.

**Suggested fix.** Set `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` on the list so
paths trim with an ellipsis; the row tooltip already carries the full path.

---

### UI-049 — Statistics strip labels are malformed

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF statistics strip is gone; the Results Summary tab is new.
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:59-76`,
  `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:226-264`

**Problem.** The strip reads `Renamed Types:4 Methods:6 …`, `Metadata Metadata removed:46`
and `Total Total:96`. The space after the colon is lost (it is trailing whitespace in a
`StringFormat`), and two groups repeat their own name as the chip label.

**Suggested fix.** Space label and value with a margin rather than a trailing space. Label the
metadata chip "Items removed" and show Total as a single value without a chip label.

---

### UI-050 — Unrenamed types show as a rename to themselves

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ResultsStore` / `SymbolIndex` (plan Task 12): an unrenamed parent is a container, not a rename; tests in `ResultsStoreTests` ("UI-050").
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:302-308`,
  `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:140-145`

**Problem.** When members of a type were renamed but the type itself was not (for example
with Preserve public API), a parent node is created with the type name as both original and
obfuscated name. It renders as
``Settings.Core.SettingsServiceBase`1 -> Settings.Core.SettingsServiceBase`1`` with the right
side in the accent colour, which looks like a rename.

**Suggested fix.** Mark such nodes as not renamed and show only the name (muted, or with a
"not renamed" hint). Exclude them from "Copy obfuscated".

---

### UI-051 — Tabs repeat their own heading and Preview shows the path twice

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF tabs are gone; Results has Summary, Symbols, Preview and Log tabs.
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/OutputPanel.xaml:26`,
  `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:28`,
  `Src/Obfy.UI/Views/MainWindow.xaml:238-253`

**Problem.** The Output tab opens with an "Output" heading and the Results tab with
"Obfuscation Results", each directly under the tab of the same name. In Preview, the combo box
shows the full output path and the line below repeats it.

**Suggested fix.** Drop the two headings and keep the toolbars (the elapsed time can sit next
to the Results buttons). In Preview show the file name in the combo with the path as a
tooltip, and remove the second line once a file is selected.

---

### UI-052 — Commands are enabled with nothing to act on

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `RunCoordinator` (plan Task 13) exposes each blocker as explicit state with its reason, which the run bar shows; tests in `RunCoordinatorTests` ("UI-052"). The row-menu part is replaced by the new file table, whose actions are enabled only when they apply; re-verify the row actions on the page.
- **Priority:** P3 · **Evidence:** Observed (Export Map, Clear files); Code (the rest)
- **Where:** `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:393-410`,
  `Src/Obfy.UI/ViewModels/FilesViewModel.cs:149-153,205-218,252-257`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:988-996`,
  `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:84-98`

**Problem.**

- Export Map is enabled before any run and would save an empty map.
- Clear (files) is enabled with an empty list. Clear and Copy for the log have no
  `CanExecute` either.
- In the file row menu, Open output, Reveal in Explorer and Preview output are enabled on rows
  that have no output and answer with a warning toast.
- "Copy path" copies the output path after a run and the input path before one, under the same
  label.

**Suggested fix.** Add `CanExecute` to each, driven by whether there is a symbol map, a file,
a log entry or an output. Split the menu item into "Copy input path" and "Copy output path".

---

### UI-053 — Merge output name and symbol map folder ignore the include checkbox

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `InputSession` (plan Task 10) and `RunCoordinator` (plan Task 13): the output name, symbol-map location, mode hint and the run follow the included files by one rule; tests in `InputSessionTests` and `RunCoordinatorTests` ("UI-053").
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/FilesViewModel.cs:476-500`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:143-154,550-553`

**Problem.** Rows can now be unchecked, but two helpers still look at the whole list:

- `ResolveMergeOutputPath` names the merged output after the first assembly in the list, even
  when that row is unchecked or skipped.
- `ResolveSymbolMapPath` falls back to the folder of `Files[0]`, which can be a skipped
  project row.

Also, the output hint and the run disagree about merging: the hint shows the merge location
when two or more included files are assemblies, but the run merges only when *every* included
file is an assembly. With two DLLs and a `.cs` file the hint says merge and the run does a
closed set.

**Suggested fix.** Use the first *included* assembly in both helpers, and share one
"will this run merge?" check between the hint and the run. Add unit tests for an unchecked
first row.

---

### UI-054 — Re-including a skipped solution row loses its reason

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `InputSession` (plan Task 10): a skipped row keeps the reason the analysis gave it when re-excluded; test in `InputSessionTests` ("UI-054").
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Models/AssemblyFile.cs:79-99`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:579-584`

**Problem.** A project that was skipped when a solution was loaded (for example, no built
output) has a checkbox like any other row. Checking it clears the skip reason and marks the
row included, although its path is still the project file and there is nothing to obfuscate.
During a closed-set run it is then skipped again with the wrong message, "Source files are not
part of a closed-set session". Unchecking it afterwards shows "Excluded"; the original reason
is gone for good.

**Suggested fix.** Keep the analyzer's skip reason separately from the user's choice. Disable
the checkbox on rows that cannot be included and show why.

---

### UI-055 — Choosing Custom again discards exclusions added on a preset

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ProtectionSession` (plan Task 9): choosing Custom again keeps the exclusions added on a preset; regression test in `ProtectionSessionTests`.
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/ViewModels/SettingsViewModel.cs:338-357,655,673`

**Problem.** Leaving Custom takes a snapshot of every setting, and choosing Custom again
restores all of it, including the exclusion, inclusion and embedding lists. Editing those
lists does not raise a property change, so it neither switches to Custom nor refreshes the
snapshot. The sequence Custom → Aggressive → add an exclusion → Custom silently removes the
exclusion just added.

**Suggested fix.** Snapshot and restore only the values presets control (the same set as
UI-039), and leave lists, watermark, signing and profile alone.

**Done when.** A unit test for the sequence above keeps the added exclusion.

---

### UI-056 — Symbol parsing and search run on the UI thread and scale badly

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ResultsStore` / `SymbolIndex` (plan Task 12): symbols are indexed and searched off the calling thread and served in pages; tests in `ResultsStoreTests` ("UI-056").
- **Priority:** P3 · **Evidence:** Code (not measured)
- **Where:** `Src/Obfy.Core/Models/SymbolMapKey.cs:33-66`,
  `Src/Obfy.UI/ViewModels/ResultsViewModel.cs:98-109,276-375`

**Problem.** UI-031 added a debounce and parses keys once, but the work is still on the UI
thread and two parts are quadratic:

- `SymbolMapKey.ParseAll` tests every member key against every type name until one matches.
- `ApplyFilter` calls `MemberMatches` / `TypeSelfMatches`, each of which scans the whole
  symbol list, once per symbol.

The tree is also still filled one node at a time. Fine for a small library; an assembly with
tens of thousands of symbols will stall when results load and on each search.

**Suggested fix.** Find a member's type by walking its dotted prefixes against a set of type
names. Precompute members per type for the filter. Build the node list off the UI thread and
swap it in once. Measure with a 50,000-entry map before and after.

---

### UI-057 — No splitter between the file list and the tabs

- **Status:** Closed (rewrite)
- **Hybrid UI:** The WPF layout is gone; the Results screen has its own layout.
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Views/MainWindow.xaml:154-159`

**Problem.** The right column is fixed at one part file list to two parts tabs. The old
layout had splitters; this one has none, so a user with a forty-project solution cannot give
the list more room, and one reading a long log cannot shrink it.

**Suggested fix.** Add a `GridSplitter` between the two rows with sensible minimum heights
(this also helps UI-010).

---

### UI-058 — Drop overlay omits .json and appears during a run

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `InputSession` (plan Task 10): `.json` is a drop type and a configuration loads instead of being added as an input; test in `InputSessionTests` ("UI-058"). The WPF drop overlay is gone: the host now rejects input while a run is in progress (`RunCoordinator.EnsureNotRunning`: `files.pick`, `files.remove`, `files.clear`, `files.setIncluded`, `settings.openConfig` and the config-load path throw `run.blocked`; a native drop is refused with a warning notice), and the page shows a `shell.dragState` overlay that says files cannot be added during a run. Tests in `RunCoordinatorTests` ("Input_IsRefusedWhileARunGoes_AndAllowedWhenIdle") and `Shell.test.tsx`. Real-drag behaviour during a run is still a manual check.
- **Priority:** P3 · **Evidence:** Code
- **Where:** `Src/Obfy.UI/Help/HelpCatalog.cs:12-13`,
  `Src/Obfy.UI/Views/MainWindow.xaml.cs:28-67`,
  `Src/Obfy.UI/ViewModels/MainViewModel.cs:972-986`,
  `Src/Obfy.UI/Views/Controls/FilesPanel.xaml:12,43-53`

**Problem.**

- A `.json` configuration can be dropped on the window, but the overlay and the empty state
  list only assembly, source, solution and project extensions.
- During a run the overlay still appears and the cursor shows "copy", but the drop is
  silently ignored.
- Dropping several configurations loads them all without awaiting, so which one wins is not
  defined.
- The Files panel keeps its own drop handlers and overlay underneath the window-level ones.

**Suggested fix.** Mention configuration files in the overlay, reject drags while a run is in
progress, load only the last configuration dropped, and remove the panel-level handlers.

---

### UI-059 — Results tab has no before-run state

- **Status:** Fixed (hybrid)
- **Hybrid UI:** `ResultsStore` (plan Task 12): an explicit before-run state; test in `ResultsStoreTests` ("UI-059"), and `RunningView.tsx` / the Results screen show it.
- **Priority:** P3 · **Evidence:** Observed
- **Where:** `Src/Obfy.UI/Views/Controls/ResultsPanel.xaml:28-33,84-98`

**Problem.** Before any run the Results tab shows "Completed in 00:00.000", a live search box
and "No symbols were renamed." — statements about a run that has not happened.

**Suggested fix.** Add a "has results" state. Before the first run (and while one is in
progress) show "Run an obfuscation to see results" and hide the elapsed time, search box and
export buttons.
