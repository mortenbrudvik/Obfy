# Obfy desktop UI: hybrid shell and redesign

Date: 2026-11-20
Branch: `hybrid-ui` (worktree `.worktrees/hybrid-ui`)
Status: awaiting approval

## Intent

Replace the WPF-UI Fluent interface of `ObfyUI.exe` with a thin WPF shell that hosts a React page in WebView2, and redesign the interface from scratch.

The owner's reasons, in their words: the UI should be "more flexible than WPF UI", "easy to use, intuitive", "easy to get started", and should "provide info on the different options".

Today's screen works against all four. Twenty expander cards share a 300px column, option help lives in tooltips or nowhere, the first launch shows an empty tool with no direction, and 24 open backlog items (UI-036 to UI-059) are mostly layout defects that are hard to fix in WPF-UI.

## Goals

1. **Easy to get started.** A first-time user can protect an assembly in two actions: drop a file, press Obfuscate. The first screen says what to do.
2. **Intuitive.** The interface follows the job in order: what to protect, how to protect it, what happened. One primary action is always visible, and when it cannot run it says why.
3. **Options explain themselves.** Every technique and every option shows what it does, what it costs and when it is unavailable, at the place where it is set.
4. **Flexible.** Layout and content are plain React and CSS, so screens can be rearranged without fighting a control library. Behaviour lives in C# without WPF types, so it is testable without a window.
5. **Same house pattern as Murmur, Ma and Sora**, so the apps stay maintainable as a family.
6. **No loss of capability.** Everything the current UI can do remains possible (inventory under "Behaviour to preserve").

## Non-goals

- Changes to obfuscation behaviour, `Obfy.Core`, the CLI, or the `obfy.json` format.
- New product features: run history, a "copy as CLI command" action, in-app updates, a theme picker (the app follows the system theme, as today).
- Syntax highlighting or a code editor in Preview. It stays plain monospace text.
- Moving Obfy to the hybrids' logging and test stack. Obfy keeps NLog (`Logging.Core`), `IHost` + Autofac, xunit 2 + Moq + Shouldly, and `Src/` / `Tests/` casing.

## Anti-goals

- A React copy of the current screen. Porting the 20-card column into HTML would miss the point.
- Business logic in the page. The page is a view; C# owns state and rules.
- A build that needs Node for .NET-only work, or an installed app that can ship without its web assets.
- Shipping both UIs. The WPF views are removed on this branch before merge.

## Constraints

- `Src/Obfy.UI/Obfy.UI.csproj`, `AssemblyName=ObfyUI` and `Images/app.png` stay where they are. `MsixPackageTests`, `AppxManifest.xml`, `ObfySetup.iss`, both build scripts and the UI test locator depend on them.
- `TreatWarningsAsErrors` applies to every new project. Package versions go in `Directory.Packages.props`.
- `Tests/Obfy.Tests/CiUiWorkflowTests.cs` pins the CI "UI tests" step (the AutomationTests csproj path and `--filter "Category=UI"`). Keep that step valid.
- `build-msix.ps1` fails if any `.pdb` is in the layout. The web build must emit none.
- WebView2 needs a writable user-data folder; the MSIX install directory is read-only.
- Work happens in the worktree. Commits carry no Claude attribution.

## Approaches considered

**A. Bridge over the existing ViewModels.** Keep the six ViewModels in the host and expose their properties and commands through the bridge. Cheapest, and the ~190 ViewModel tests survive untouched. Rejected: the page's contract would be the old screen's shape (INPC properties, `ObservableCollection`s, `ICommand` enablement), which makes a from-scratch redesign awkward and leaves the logic tied to the WPF target and WPF-UI dialog types.

**B. House pattern: a WPF-free application layer behind request handlers. (Chosen.)** Lift the ViewModel logic into plain session classes in a new project with no WPF reference. The page calls them through the `apibridge` host object and follows pushed events. Costs a port of the logic and its tests, but the logic is lifted class by class rather than rewritten, the tests port almost one to one, and the result matches Murmur, Ma and Sora.

**C. Leave WPF entirely (Electron, Tauri, Photino).** Rejected: the request is WPF and WebView2, and the installer, MSIX identity and in-process use of `Obfy.Core` all favour staying in .NET.

The owner asked for the design to be presented rather than to choose between approaches, so B is the designer's call.

## Architecture

### Projects

```
Src/
  Obfy.Common/     JsonUtils (camelCase, string enums), AsyncUtils            net10.0
  Obfy.App/        application layer: sessions, run coordinator, Features/    net10.0, no WPF
  Obfy.WebView/    WebViewControl, WebViewHandler, IWebViewInterop            WPF, only project referencing Microsoft.Web.WebView2
  Obfy.Interop/    ApiBridge, ApiRegistry, ApiRegistrations, ApiEventAggregator, IpcNames, IpcLogger
  Obfy.UI/         WPF shell: App, MainWindow, MainViewModel, AppModule, FileDialogs, ThemeController
  WebApp/          React page
Tests/
  Obfy.App.Tests/      ported ViewModel tests
  Obfy.Interop.Tests/  bridge, registry, events, contracts
  Obfy.UI.Tests/       shell only (startup arguments, DI wiring, drop handling)
  Obfy.UI.AutomationTests/  end-to-end, Category=UI
```

References: `UI -> WebView, Interop, App`; `Interop -> WebView, App, Common`; `App -> Obfy.Core, Logging.Core, Common`; `WebView -> Common`.

`Obfy.App` plays the role the hybrids call "Core". It is a separate project because `Obfy.Core` is the obfuscation engine and ships in the CLI and NuGet tool; it must not gain a mediator dependency.

New packages: `Microsoft.Web.WebView2`, `Mediator.Abstractions`, `Mediator.SourceGenerator` (MIT; generator in `Obfy.UI` only). WPF-UI stays for `FluentWindow` and the title bar. CommunityToolkit.Mvvm stays for the one shell ViewModel.

### Bridge

Identical to the house pattern.

- Page to host: `window.chrome.webview.hostObjects.apibridge.InvokeMethod(service, method, jsonParams)`, returning a JSON envelope `{ success, data }` or `{ success: false, error: { code, message } }` (Sora's typed error form).
- Host to page: `PostWebMessageAsJson` with `{ eventName, data }`, marshalled to the UI thread with `BeginInvoke`.
- Each endpoint is a Mediator `record XRequest : IRequest<T>` with its handler in the same file under `Obfy.App/Features/<Area>/`, registered by name in `ApiRegistrations.RegisterAll`. Names live in `IpcNames` and are mirrored by hand in `WebApp/src/interop`.
- Handlers run off the UI thread.
- **Paths.** The page refers to *input* files by id; a file to read enters the host only through a native picker, a drop, a startup argument or a loaded configuration, never as a path string from the page. The two *output destinations* are the exception: the user may type the output folder and the symbol-map location (as today's free-text box allows, and as UI-045 requires), so `output.set` and the symbol-map field accept a string, and the host validates and canonicalises it before use (a relative symbol-map path resolves against the output folder; typing clears the session `-o` default). This keeps the input surface closed while preserving the typed-output behaviour. One further page-writable path is read by the engine: `signing.keyFile` (a small, intentional exception that matches the old UI; the key is never echoed back to the page).

### Application layer (`Obfy.App`)

State lives in C#. The page reads it once and then follows pushes.

| Service (from) | Owns | Calls | Pushes |
|---|---|---|---|
| `ProtectionSession` (`SettingsViewModel`) | all settings, level and Custom snapshot, runtime-profile availability, validation, loaded configuration path and dirty flag | `settings.get`, `settings.update` (patch), `settings.applyLevel`, `settings.openConfig`, `settings.saveConfig`, `settings.pickSigningKey` | `settings.changed` |
| `InputSession` (`FilesViewModel`) | file rows, solution and project expansion, include state, output folder, symbol-map option, preferences | `files.get`, `files.pick`, `files.remove`, `files.clear`, `files.setIncluded`, `files.reveal`, `files.openOutput`, `output.set`, `output.pickFolder` | `files.changed` |
| `RunCoordinator` (`MainViewModel`) | run-mode selection, the run loop, cancel, blocked reason, progress, per-file status | `run.get`, `run.start`, `run.cancel` | `run.changed`, `run.completed` |
| `LogBuffer` (`OutputViewModel`) | log lines with run separators | `log.get`, `log.clear` | `log.appended` (batched, about every 100 ms) |
| `ResultsStore` (`ResultsViewModel`) | statistics, symbol tree, report, preview | `results.summary`, `results.symbols` (query, parent, offset, limit), `results.exportMap`, `results.exportReport`, `preview.get` | `results.changed` |
| shell | version, clipboard, readiness | `app.info`, `app.ready`, `shell.copyText` | `shell.dragState`, `app.notice` |

`settings.get` returns one snapshot: the values, plus what the page needs to draw without knowing the rules. Per technique: `enabled`, `available`, `unavailableReason`, `differsFromLevel`. Plus `level`, `validationErrors`, `configPath`, `dirty`. `run.get` likewise carries `canStart`, `blockedReason`, `mode` (single, sources, merge, closed set) and `modeExplanation`, `outputHint`.

Native pickers sit behind an `IFileDialogs` interface in `Obfy.App`, implemented in `Obfy.UI`. Reveal-in-Explorer and clipboard are host calls.

### Logic defects fixed during the lift

The lift is the natural moment to settle the open logic items rather than port them:

- **UI-039 / UI-055:** the level changes to Custom only when a setting that presets govern changes. Runtime profile, watermark, signing, packing, merge, embedding, incremental cache and the inclusion and exclusion lists are independent of level and are never touched by a Custom restore.
- **UI-040:** validation errors are part of the settings snapshot and the page shows the required field as soon as its technique is on.
- **UI-044 / UI-056:** decompile and symbol parsing run off the UI thread; the symbol tree is served in pages.
- **UI-045:** the symbol-map path is editable only when the option is on, has a Browse button, and relative paths resolve against the output folder.
- **UI-050:** unrenamed parent types are shown as containers, not as renames.
- **UI-052:** every action has an explicit enabled state and reason in the snapshot.
- **UI-053:** output naming honours the include checkbox, and the mode hint and the run use one rule (`RunCoordinator` computes both).
- **UI-054:** a skipped row keeps its original reason when re-included and excluded again.
- **UI-058:** a `.json` drop is a recognised type in the overlay; only the first dropped configuration loads and a notice says so.
- **UI-059:** Results has an explicit before-run state.

Layout items (UI-036, 037, 041, 042, 043, 046 to 049, 051, 057) are closed by the new page and recorded as such in `backlog.md`.

### WPF shell (`Obfy.UI`)

- `MainWindow`: `ui:FluentWindow`, native `ui:TitleBar`, solid palette background (no Mica behind the page), the web view below, and a native failure panel (explanation, log folder, Restart, Quit) shown if WebView2 fails to start or dies three times.
- `WebViewHandler` copied in shape from Ma: exact origin allowlist, new windows blocked, context menu only in editable fields and over selected text in Release, DevTools only in Debug, reload on process failure.
- Debug loads `http://localhost:5250/` (Vite, `strictPort`). Release maps `https://obfy.localassets.invalid/` to `Assets\` under `AppContext.BaseDirectory` with `DenyCors`. A content security policy (`default-src 'self'` and friends) is injected at build.
- User data folder: `%LOCALAPPDATA%\Obfy\WebView2`.
- `ThemeController`: follows the system theme and applies it to WPF-UI, the palette dictionary, the web view's background colour and `PreferredColorScheme` together.
- The window is revealed when the page calls `app.ready`, with a timeout, so there is no white flash.
- Startup arguments (`ObfyUI.exe a.dll b.sln -o out`) are applied to `InputSession` before the page loads.
- A missing WebView2 runtime produces a native message with the download link.

**File drops.** The window keeps `AllowDrop` and reads `DataFormats.FileDrop` natively, then feeds the same path as the file picker; the page shows its overlay from `shell.dragState`. Whether a drop over the web area reaches the WPF window is unverified in the owner's other apps, so the first implementation task is a spike. If it does not, the fallback is `AllowExternalDrop` with the page forwarding the dropped file objects (`postMessageWithAdditionalObjects`) so the host reads the real paths.

### Web app (`Src/WebApp`)

Ma's stack: npm, Vite, React 19, TypeScript (strict), ESLint flat config, Vitest with jsdom and Testing Library. Plain CSS with tokens on `:root`. No router, state library, component library, icon set or web fonts.

```
src/
  interop/    WebViewApiClient.ts, api.ts (typed wrappers), errorMessage.ts, mock bridge
  hooks/      useWebViewEvent, usePushedState, useAppAction, usePrefersReducedMotion
  types/      domain.ts (hand-mirrored DTOs), WebView.ts
  components/ Toggle, Select, Slider, Field (label + control + help), Tabs, Meter, Badge, SplitPane, Notice, Keycap
  content/    techniques.json, guide.json
  features/   shell/ files/ protection/ results/ guide/   (each: Page.tsx, Page.css, hooks, __tests__/)
  index.css   tokens, light and dark, forced-colors, focus ring
```

A mock bridge (Sora's idea) answers every call in memory so the built page runs in a plain browser with `npm run preview` and under Vitest.

**Content as data.** `content/techniques.json` holds, per technique and per option: name, one-line summary, what it does, trade-offs, option help, and the Guide topic it links to. `content/guide.json` replaces `HelpCatalog.cs`. Rules stay in C#; words stay in the page. A C# contract test reads `techniques.json` and fails if its technique and option ids differ from the settings the application layer exposes, so an option can never ship without its explanation.

## The interface

A sidebar of three steps in the order of the job, a Guide, and one always-visible run bar.

```
┌──────────────────────────────────────────────────────────────────────┐
│ Obfy                                                        ─  □  ✕  │
├───────────┬──────────────────────────────────────────────────────────┤
│ Files   3 │                                                          │
│ Protection│                    current page                          │
│  Standard │                                                          │
│ Results ● │                                                          │
│           │                                                          │
│           │                                                          │
│ Guide     ├──────────────────────────────────────────────────────────┤
│ Obfy 2.0.0│ 3 of 4 files · Standard · obfy-out      [ Obfuscate ]    │
└───────────┴──────────────────────────────────────────────────────────┘
```

**Run bar.** Present on every page. It summarises what will happen (files, level, output), carries the Obfuscate button (Ctrl+Enter), and when the run cannot start it shows the reason in plain words instead of a disabled button with a tooltip. During a run it shows the current file, progress and Cancel (Esc).

**Files.** First launch shows one question ("What do you want to protect?"), a drop zone with Add files and Open solution, the accepted types, and a three-step "how it works" strip. With files it becomes a table (include, name, origin, kind, size, status, row menu) with skipped rows and their reasons, a sentence explaining the run mode ("These 3 assemblies are obfuscated together as a closed set, so references between them keep working"), and an Output card (folder with a truthful placeholder, Browse, symbol-map toggle with an explanation of why it matters).

**Protection.** Three layers, from least to most detail:

1. *Level cards*: Minimal, Standard (recommended, preselected), Aggressive, Custom. Each says what it turns on and shows three small meters: protection, runtime cost, compatibility risk.
2. *Target runtime*, with a line explaining that unsupported protections are switched off for you.
3. *Techniques* as a master and detail split. The list groups all techniques (Names, Data, Code, Runtime defences, Packaging, Housekeeping, Scope) as rows with a toggle, a one-line summary and badges ("Windows only", "Changed", "Not available for NativeAOT"), and has a search box that filters techniques and options. The detail pane for the selected technique shows what it does, what to know before using it, each option with its help text permanently visible beneath it, a before-and-after snippet where one helps, the honest security note on the encryption techniques, and a link into the Guide.

The configuration file (`obfy.json`) is shown in the page header with its unsaved state and Open and Save.

**Results.** Before a run it says what will appear. During a run it shows per-file status and the live log. Afterwards it selects itself and shows the outcome with Open output folder, Export report and Export symbol map, and four tabs: Summary (statistic tiles, zero counters absent, per-file outputs with Reveal), Symbols (search and a paged tree, original to obfuscated), Preview (choose an output, decompiled off-thread), Log (problems only, timestamps, wrap, copy, run separators).

**Guide.** The help topics in a two-pane page, opened from the sidebar, F1, or "Read more" links in technique details.

**Visual language.** The house style: solid flat panels, hairline borders, 14px Segoe UI Variable, small radii, one accent taken from the Obfy icon, Cascadia Mono for names, code and log. Light and dark follow the system; high contrast (`forced-colors`) and reduced motion are honoured. Below about 1000px the technique detail opens as an overlay panel instead of a column.

**Keyboard and accessibility.** Ctrl+O add files, Ctrl+S save configuration, Ctrl+L open configuration, Ctrl+Enter obfuscate, Esc cancel, F1 Guide, Ctrl+C in log and symbols, Delete in the file table. Every control is a native element with a label; status changes go to a polite live region; focus is always visible.

A clickable mockup accompanies this document as a separate web page (published, not committed to the repo). It illustrates this prose with one concrete realisation of the visual details the text gives only in outline — the accent colour, the level-card meters, the before-and-after snippets, the master/detail density, and the empty and first-launch states. Where the two differ, this document governs the behaviour and the mockup governs the look.

## Behaviour to preserve

Run-mode selection (merge, closed set, per-file, joint source compilation); empty-output rules (`*.obfuscated.*`, `obfy-out`); profile gates that disable a protection but keep its stored value; watermark id required when watermark is on; drops rejected during a run; a dropped `.json` loads a configuration; solution and project expansion with skipped rows; `SymbolMapKey` nesting; preferences file and `OBFY_UI_PREFERENCES`; a session `-o` not saved as the default; configuration round-trip keeping unmapped fields; the log separator between runs; a successful run selects Results; report export as HTML or JSON; logs under `%LOCALAPPDATA%\Obfy\Logs\`.

## Build, packaging, CI

- `Obfy.UI.csproj` gains Sora's incremental targets: `NpmInstall` (keyed on `package-lock.json`), `BuildFrontend` (keyed on web sources), `CopyFrontend` to `$(TargetDir)Assets\`, and a publish step that adds `dist` to the publish output and fails the publish if `dist` is empty. `-p:SkipFrontendBuild=true` skips them. Foreign-architecture WebView2 loaders are removed from win-x64 publishes. Source maps are off in production builds.
- `scripts/dev.ps1` starts Vite, waits for the port, then runs the shell with `SkipFrontendBuild`.
- `.gitignore`: `node_modules/`, `Src/WebApp/dist/`.
- CI: add Node setup, `npm ci`, `npm run lint`, `npm run test:run` to the Windows job. The Linux CLI job is unaffected.
- Inno installer: check for the WebView2 runtime and run the Evergreen bootstrapper silently when it is absent (git-diff's recipe).
- MSIX: no bundling (WebView2 is part of Windows 11 and present on supported Windows 10); the missing-runtime message covers the rest.

## Testing

- **`Obfy.App.Tests`**: the ViewModel tests, ported to the session classes. Same stack as today.
- **`Obfy.Interop.Tests`**: bridge envelope and error mapping, registry, event aggregator, origin allowlist statics, a `[Theory]` listing every `(service, method, request type)`, JSON contract tests for the shapes the page sends and receives, and the `techniques.json` contract test.
- **`Obfy.UI.Tests`**: startup argument parsing, DI container resolves, drop filtering. XAML-contract and converter tests are deleted with the XAML they cover.
- **Web**: Vitest next to the code, never against the real bridge.
- **End to end (`Category=UI`)**: FlaUI cannot see inside a web view. The suite becomes a small set driven through WebView2's remote debugging port with Playwright for .NET: launch, first-launch screen, toggle a technique, open the Guide with F1, and the obfuscate flow from startup arguments to an output file. One FlaUI test keeps covering window launch and chrome.
- A bug fix starts with a failing test.

## Delivery

One branch, four stages, each leaving the solution building and tests green:

1. **Skeleton.** New projects, shell window, bridge, a page that proves a call and an event, build targets, dev script, and the file-drop spike.
2. **Application layer.** Lift the five ViewModels into `Obfy.App` with their tests and the defect fixes above; register the endpoints.
3. **Page.** Shell and run bar, then Files, Protection, Results, Guide.
4. **Cut-over.** Remove the WPF views, ViewModels and converters; installer, MSIX and CI changes; end-to-end tests; update `CLAUDE.md`, `README.md`, `CONTRIBUTING.md`, `docs/Testing.md` and `backlog.md`.

## Left to the builder

- Exact DTO field names and the split of endpoints into request files.
- Component internals and CSS structure within the token set.
- The wording of technique explanations, drawing on `CLAUDE.md`, the README and `HelpCatalog.cs`, in calm sentence-case copy.
- Meter values per level, derived from what each preset enables.
- The batching interval for log pushes and the page size for symbols.

## Designer's calls (not the owner's decisions)

Approach B; a separate `Obfy.App` project; keeping `IHost`, NLog and the xunit 2 stack; technique and Guide text as JSON in the web app; Playwright over the debugging port for end-to-end tests; dev port 5250; fixing the listed logic defects during the lift; removing the old UI on this branch.
