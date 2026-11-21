# Obfy - C# Obfuscation Tool

Obfy is a professional .NET obfuscation tool that protects C# assemblies and source code through various obfuscation techniques.

## Codebase Structure

```
Obfy/
├── Src/
│   ├── Obfy.Console/          # CLI entry point (System.CommandLine)
│   ├── Obfy.UI/               # WPF shell hosting WebView2; composition root
│   │   ├── Views/             # ShellWindow (title bar, WebView2, native failure panel)
│   │   ├── ViewModels/        # ShellViewModel (the only ViewModel)
│   │   ├── Services/          # Theme sync, native file dialogs, startup command line
│   │   └── DependencyInjection/ # Autofac modules, HybridBootstrap (Mediator + bridge wiring)
│   ├── Obfy.App/              # WPF-free UI logic: sessions and Mediator requests/handlers
│   │   ├── Sessions/          # ProtectionSession, InputSession, LogBuffer, ResultsStore, RunCoordinator
│   │   └── Features/<Area>/   # One folder per bridge area: App, Files, Log, Output, Preview, Results, Run, Settings
│   ├── Obfy.Interop/          # The bridge: ApiBridge, ApiRegistry, ApiRegistrations, IpcNames, event aggregator
│   ├── Obfy.WebView/          # WebView2 control + handler (virtual host, allowed origins, host object)
│   ├── Obfy.Common/           # JsonUtils (camelCase IPC/config JSON)
│   ├── WebApp/                # React + Vite + TypeScript page (not a .NET project)
│   │   └── src/               # features/{files,protection,results,guide,shell}, components, interop, content
│   ├── Obfy.Core/             # Core obfuscation logic
│   │   ├── DependencyInjection/  # Autofac module registration
│   │   ├── Models/            # Settings and result models
│   │   ├── Obfuscators/       # Obfuscation implementations
│   │   │   ├── Assembly/      # dnlib-based assembly obfuscators
│   │   │   └── Source/        # Roslyn-based source obfuscators
│   │   ├── Pipeline/          # Obfuscation pipeline orchestration
│   │   ├── Services/          # Core services
│   │   └── Utilities/         # Helper classes (NativePacker, ManagedLauncherPacker)
│   ├── Obfy.NativeHost/       # Prebuilt win-x64 CLR-host stub (not in Obfy.sln)
│   ├── Obfy.PackedBootstrap/  # Managed bootstrap loaded by the native host
│   ├── Obfy.VisualStudio/     # Visual Studio 2022 extension (C#)
│   ├── Obfy.VSCode/           # VS Code stub (schema + problem matcher)
│   ├── Settings.Core/         # JSON settings persistence
│   └── Logging.Core/          # NLog logging infrastructure
├── Tests/
│   ├── Obfy.Tests/            # Core unit tests
│   ├── Obfy.Console.Tests/    # CLI parsing & integration tests
│   ├── Obfy.App.Tests/        # Session + handler unit tests (UI logic, no WPF)
│   ├── Obfy.Interop.Tests/    # Bridge, registry, events, request JSON + content contract tests
│   ├── Obfy.UI.Tests/         # Shell wiring, startup command line, host service
│   ├── Obfy.ScenarioTests/    # SDK fixtures + platform (Blazor/AOT/MAUI/Unity)
│   ├── Obfy.VisualStudio.Tests/ # VS helpers without a hive
│   └── Obfy.UI.AutomationTests/ # Locator tests + Playwright-over-WebView2 e2e + one FlaUI smoke (Category=UI)
├── scripts/
│   └── dev.ps1                # Dev loop: Vite dev server + the shell
├── docs/                      # Documentation
├── package/                   # MSIX manifest and Store assets
├── build/                     # Build scripts and installer
│   ├── build-installer.ps1    # Installer build automation
│   ├── build-msix.ps1         # MSIX package for Store / sideload
│   ├── generate-msix-assets.ps1 # Store logo/splash assets from app.png
│   ├── ObfySetup.iss          # Inno Setup script
│   └── generate-icon.ps1      # Icon generation
└── .claude/                   # Claude Code configuration
```

## Build Commands

```bash
# Build solution
dotnet build Obfy.sln

# Build release
dotnet build Obfy.sln -c Release

# Run tests
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj
dotnet test Tests/Obfy.App.Tests/Obfy.App.Tests.csproj
dotnet test Tests/Obfy.Interop.Tests/Obfy.Interop.Tests.csproj
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj
dotnet test Tests/Obfy.ScenarioTests/Obfy.ScenarioTests.csproj
dotnet test Tests/Obfy.VisualStudio.Tests/Obfy.VisualStudio.Tests.csproj
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj --filter Category=UI   # needs a Release build of Src/Obfy.UI
(cd Src/WebApp && npm run test:run)   # web (Vitest) tests; also: npm run lint

# Run CLI
dotnet run --project Src/Obfy.Console/Obfy.Console.csproj -- --help

# Install as a .NET tool
dotnet tool install --global Obfy
# or: dotnet pack Src/Obfy.Console/Obfy.Console.csproj -c Release -o ./artifacts
#     dotnet tool install --global Obfy --add-source ./artifacts

# Run UI for development (Node 24 + PowerShell 7): starts Vite on :5250, then the shell with
# -p:SkipFrontendBuild=true. The Debug shell loads http://localhost:5250 (hot reload, DevTools)
.\scripts\dev.ps1

# Run UI from a Release build (serves the built page from Assets\ beside ObfyUI.exe)
dotnet run --project Src/Obfy.UI/Obfy.UI.csproj -c Release

# .NET-only work: skip the npm steps (Obfy.UI.csproj otherwise runs npm ci + npm run build before Build)
dotnet build Obfy.sln -p:SkipFrontendBuild=true

# Build MSIX (sideload identity)
.\build\build-msix.ps1

# Build unsigned Store MSIX (Partner Center identity from package/store-identity.json)
.\build\build-msix.ps1 -Store
```

## CLI Usage

```bash
# Basic obfuscation
obfy input.dll -o output/

# Solution (closed set)
obfy MyApp.sln -o out/

# With specific level
obfy input.dll -l aggressive

# With config file
obfy input.dll -c obfy.json

# Generate config
obfy config generate -o obfy.json

# Interactive wizard
obfy config wizard -o obfy.json

# Multiple files
obfy file1.dll file2.dll -o output/

# Merge assemblies into one
obfy App.dll Lib.dll --merge -o output/
```

## UI Application

The desktop app is a hybrid: a thin WPF shell hosts WebView2, which shows a React page. All UI state and rules live in WPF-free `Obfy.App` sessions; the page renders snapshots and sends commands over the bridge. WPF-UI is used only for the window frame (`FluentWindow` + `TitleBar`).

- **Shell** (`Obfy.UI`): `ShellWindow` is the title bar, the WebView2 and a native failure panel (Restart / Open log folder / Quit) shown instead of a blank page when WebView2 cannot start or fails for good. `ShellViewModel` is the only ViewModel. `ThemeController` follows the Windows light/dark mode for the window and the page. Native file drop is forwarded into the bridge (`files.dropPaths`; the page never calls it). The shell blocks navigation outside its own origin, so the online guide is opened in the default browser by the host (`app.openGuide`), never by a link or `window.open`
- **WebView2 Runtime**: the Evergreen runtime is required. `App` shows a message with the download link when it is missing; the installer bootstraps it (`build/ObfySetup.iss`, `build-installer.ps1` downloads it into `build\redist`)
- **Sessions** (`Obfy.App/Sessions`): `ProtectionSession` (settings, levels, the Custom snapshot, profile gates, config open/save), `InputSession` (files, solution/project expansion, include flags, output folder, symbol-map path, mode hint, drops), `LogBuffer` (run log, batched pushes), `ResultsStore` (summary, paged/searchable symbol index, preview decompile, exports), `RunCoordinator` (start/cancel, blockers with reasons, progress). They carry the rules the old ViewModels held; `Obfy.App.Tests` covers them
- **Screens** (`Src/WebApp/src/features`): a sidebar with **Files** (first-launch empty state, file table, output card with the symbol-map option), **Protection** (level cards, technique list and detail, scope), **Results** (Summary, Symbols, Preview and Log tabs; a running view while obfuscating) and **Guide** (F1: a short in-app quick-start, Getting started and Shortcuts, with a button that opens the full guide online), plus a persistent **run bar** (Obfuscate / Cancel, merge, blockers with reasons, progress). A successful run selects Results. Shortcuts: Ctrl+Enter obfuscate, F1 guide, add files, open/save configuration
- **Drag-and-drop**: The web area takes the OS drop (`AllowExternalDrop`), so the page shows its drop overlay from its own drag events and posts the dropped files to the host with `postMessageWithAdditionalObjects("files.dropped", files)`; `WebViewHandler` reads each `CoreWebView2File.Path` and the shell sends `DropPathsRequest`. The host refuses input during a run (`RunCoordinator.EnsureNotRunning`): a drop becomes a warning notice, and the picker, remove, clear, include and config-load calls fail with `run.blocked`. The window accepts `.dll`, `.exe`, `.cs`, `.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`, and `.json`. Solutions and projects expand into included outputs and skipped rows. A `.json` file loads that configuration
- **Closed-set**: Obfuscate runs a closed set when two or more included assemblies are listed, or when files came from a session (Merge still wins)
- **Empty output** (desktop app): One file writes `*.obfuscated.*` next to the input. Two or more `.cs` files compile together into `obfy-src` next to the first. A closed set writes `obfy-out` next to the first included assembly (for a solution, usually inside `bin`). Merge writes `*.obfuscated.*` next to the first input. With an output folder set, each file keeps its input name there. The run bar's hint and the run use one rule (`InputSession`). The CLI differs: `--merge` without `-o` writes `{name}.merged{ext}`, and a solution or project writes `obfy-out` next to the solution or project file (`docs/CLI.md`)
- **Level presets**: Minimal (symbol renaming and debug information removal), Standard, Aggressive, or Custom. Choosing Custom restores the snapshot taken when leaving Custom. Profile gates disable unavailable protections and keep the stored values. A loaded configuration whose preset-controlled options are not exactly its `level` preset's is shown and run as Custom (`ProtectionSession.FromObfySettings`); the file is not rewritten
- **Results**: `SymbolMapKey` nests members under the longest matching `Type:` name. Method keys include the signature (`Method:Ns.MyType.Foo(System.Void ())`). Symbols are paged and searched, and previews decompiled, off the UI thread
- **Preferences**: `%APPDATA%\Obfy\ui-preferences.json` stores the last output folder and the symbol-map option (`OBFY_UI_PREFERENCES` overrides the path). A missing restored folder is cleared. A session `-o` path is not written as the default; editing the box clears that flag
- **Dev loop**: `scripts/dev.ps1` (PowerShell 7, Node 24) starts Vite on port 5250 (`strictPort`), waits for it, then runs the shell with `-p:SkipFrontendBuild=true` and stops Vite on exit. The Debug shell loads `http://localhost:5250` and enables DevTools; the Release shell loads the built page
- **Frontend in MSBuild** (`Obfy.UI.csproj`): `NpmInstall` (`npm ci` when `package-lock.json` is newer than the last install) and `BuildFrontend` (`npm run build`, skipped while `Src/WebApp/dist/index.html` is newer than every input) run before Build; `CopyFrontend` copies `dist` to `Assets\` beside `ObfyUI.exe`; `IncludeFrontendInPublish` adds it to `dotnet publish` (an empty `dist` fails the publish). `-p:SkipFrontendBuild=true` skips the npm steps, for .NET-only work. There is no committed `Assets\` folder. The Release shell serves `Assets\` from the virtual host `https://obfy.localassets.invalid`. The installer (`{app}\UI\Assets`) and the MSIX layout carry `Assets\` (the MSIX merges the Store logos into it)

Tech stack: WPF + WebView2, React 19 + Vite + TypeScript (Vitest, Testing Library, ESLint), Mediator (source-generated; the generator is referenced only by `Obfy.UI`), WPF-UI 4.3.0 (window frame), CommunityToolkit.Mvvm (shell ViewModel), Autofac, NLog

### Hybrid architecture

- **IPC**: JS calls the `apibridge` host object (`window.chrome.webview.hostObjects.apibridge`; typed wrappers in `Src/WebApp/src/interop/api.ts`) → `ApiBridge` → `ApiRegistry` (service.method → request type) → Mediator handler in `Obfy.App/Features/<Area>`. The reply is `{"success":true,"data":...}` or `{"success":false,"error":{"code","message"}}`. The bridge never throws; handlers signal user-facing failures with `ObfyApiException`
- **Events**: pushed host → page through `ApiEventAggregator`. Sessions publish through `IEventPublisher` (in `Obfy.App`, so they do not reference the bridge). Names: `settings.changed`, `files.changed`, `log.appended`, `log.cleared`, `results.changed`, `run.changed`, `run.completed`, `app.revealed`. Each carries its service's snapshot; publishing is safe from worker threads
- **Registering**: each endpoint is registered in `ApiRegistrations`; service, method and event names live in `IpcNames`
- **Adding an endpoint**: request + handler in `Obfy.App/Features/<Area>` → register in `ApiRegistrations` (add the `IpcNames` constants) → wrapper in `api.ts` (and the types/mock bridge) → tests (`Obfy.App.Tests`, `Obfy.Interop.Tests`, Vitest). `app.openGuide` is the worked example for an endpoint that opens a web address
- **Online guide**: the full guide is at `https://brudvik.com/obfy/guide`. The page calls `api.app.openGuide(topic?)` with a section id; `OpenGuideRequestHandler` builds the `https` URL from `GuideLinks` (`Obfy.App/Features/App/GuideLinks.cs`, the one allowlist of section ids, each mapped to its `#fragment`) and calls `IShellLauncher.OpenUrl`, which accepts `https` only. An unknown or missing id opens the guide's start; the id is never echoed back, and a browser that cannot start is an `ObfyApiException` with no internal details. `brudvik.com` is deliberately not in the WebView's allowed origins
- **Paths**: the page never supplies a URL (it sends a guide section id, see Online guide) and never supplies a path for an input file (picker, drop, startup argument or loaded configuration only). The page-writable text the host does read or write is small and intentional, as in the old UI: the output folder and symbol-map location, and `signing.keyFile` (the key file the engine reads; the key is never echoed back to the page)
- **Logging**: the log file keeps Debug and above, so nothing the user typed or picked is logged. `IpcLogger` writes `service.method` and the payload length, never the payload; `ApiBridge` logs a failed call's code and exception type, not its message
- **JSON**: DTOs are camelCase via `Obfy.Common.JsonUtils` (case-insensitive reads, enums as strings)
- **Content as data**: technique copy is in `Src/WebApp/src/content/techniques.json`; the in-app Guide copy (`guide.json`) is only the quick-start, topics `start` and `keys`. `TechniqueContentContractTests` (Obfy.Interop.Tests) pins the technique ids and option keys to the host's technique list, so a technique cannot ship without content, and pins every `guideTopic` in `techniques.json` to an id in `GuideLinks.Topics` (a "Read more online" link opens that section in the browser)

## Obfuscation Techniques

### Assembly Obfuscators (dnlib)

| Technique | Priority | Description |
|-----------|----------|-------------|
| DependencyEmbedding | 8 | Packs sibling `{AssemblyRef.Name}.dll` as `Obfy.Embedded.*` resources |
| StringEncryption | 10 | Encrypts string literals (XOR or AES-256) |
| ConstantEncryption | 11 | Encrypts numeric constants (int, long, float, double; XOR or AES-256) |
| ResourceEncryption | 15 | Encrypts embedded resources (XOR or AES-256) |
| AntiDebug | 18 | Injects debugger detection (kernel32 omitted on NativeAOT / IL2CPP / Blazor WASM) |
| AntiDump | 19 | Wipes PE headers in memory and in-process `0xC3` patch of `dbghelp!MiniDumpWriteDump` (Windows X86/X64; ARM64 skipped; gated off NativeAOT / IL2CPP / Blazor WASM). External dumpers are unaffected. |
| AntiDecompiler | 20 | Injects junk types/methods and optional decoy ConfusedBy/Dotfuscator attributes |
| Watermark | 21 | Embeds a customer/build id as a pinned `WatermarkAttribute` |
| AntiTamper | 22 | Verifies assembly integrity at runtime |
| Virtualization | 24 | Bytecode interpreter for eligible instance and static methods (objects, non-generic calls, fields, newobj, ldstr; i4/i8/r4/r8). Skips EH, generic methods/types/calls, byref, custom structs/`Nullable<T>`, switch, constructors, typeof (`ldtoken`), interpolators that allocate `DefaultInterpolatedStringHandler`, and `using` / enumerator-struct foreach (array foreach can be encoded). Per-build permute+XOR; gated off NativeAOT / IL2CPP / Blazor WASM; off in every preset. Deterrent, not confidentiality. |
| MethodEncryption | 25 | XOR-encrypts method IL in the PE (Windows; per-method keys; skips generics) |
| ControlFlow | 30 | State-machine dispatch of basic blocks and/or opaque predicates |
| ReferenceProxy | 40 | Hides in-module call targets behind proxy methods |
| SymbolRenaming | 50 | Renames types, methods, fields, properties, events, namespaces |
| MetadataRemoval | 90 | Strips debug info and attributes |

Packing (post-save, not an `IObfuscator`): native win-x64 FDD CLR-host stub that replaces the managed PE; `packing.rid: portable` keeps the managed launcher. Closed-set / solution runs pack each entry-point output. Not a Pre-JIT packer.

### Source Obfuscators (Roslyn)

| Technique | Priority | Description |
|-----------|----------|-------------|
| SourceStringEncryption | 10 | Encrypts string literals in source code |
| SourceControlFlow | 30 | Adds opaque predicates and transforms control flow |
| SourceSymbolRenaming | 50 | Renames identifiers in source code |

> **Security note:** String/constant/resource/method-IL "encryption" is *obfuscation*, not confidentiality.
> The decryption key is embedded in the output assembly and is recoverable by anyone who runs or
> inspects it — XOR trivially, AES-256 with a little more effort. These techniques raise the cost of
> casual reverse engineering; they are not a substitute for encrypting secrets that must stay secret.
> Do not ship real secrets (keys, tokens, credentials) inside an assembly and rely on obfuscation.

## Architecture

### Pipeline Pattern
Obfuscators implement `IObfuscator` and execute in priority order through `ObfuscationPipeline`.

### Strategy Pattern
Each obfuscation technique is a separate strategy that can be enabled/disabled.

### Dependency Injection
Uses Autofac with module-based registration (`ObfuscationModule`).

## Key Files

| File | Purpose |
|------|---------|
| `Obfy.Core/Obfuscators/IObfuscator.cs` | Core interface for all obfuscators |
| `Obfy.Core/Pipeline/ObfuscationPipeline.cs` | Orchestrates obfuscator execution |
| `Obfy.Core/Models/ObfySettings.cs` | Complete configuration model |
| `Obfy.Console/Program.cs` | CLI entry point |
| `Obfy.UI/Views/ShellWindow.xaml` | Shell window: title bar, WebView2, native failure panel |
| `Obfy.UI/ViewModels/ShellViewModel.cs` | Shell VM: WebView2 setup (dev server vs `Assets\`), profile folder, failure-panel commands |
| `Obfy.UI/DependencyInjection/HybridBootstrap.cs` | Wires Mediator, the bridge and the sessions |
| `Obfy.App/Sessions/*.cs` | ProtectionSession, InputSession, LogBuffer, ResultsStore, RunCoordinator (all UI logic) |
| `Obfy.Interop/ApiBridge.cs` | The `apibridge` host object |
| `Obfy.Interop/ApiRegistrations.cs`, `IpcNames.cs` | Every endpoint the page may call, and the names it uses |
| `Obfy.App/Features/App/GuideLinks.cs` | Allowlist of online-guide section ids the page may open (`app.openGuide`) |
| `Obfy.Interop/ApiEventAggregator.cs` | Host-to-page event push |
| `Src/WebApp/src/main.tsx`, `App.tsx` | Page entry points; `features/shell/Shell.tsx` is the layout |
| `Src/WebApp/src/interop/api.ts` | Typed client for every bridge endpoint |
| `Src/WebApp/src/content/techniques.json` | Technique and option copy (contract-tested against the host) |
| `scripts/dev.ps1` | Dev loop: Vite on 5250 + the shell |
| `Obfy.Core/Models/SymbolMapKey.cs` | Parses symbol-map keys for the Results tree |
| `package/AppxManifest.xml` | MSIX identity, full-trust capability, CLI alias |
| `package/store-identity.json` | Partner Center Name / Publisher / Product ID for `-Store` packs |
| `PRIVACY.md` | Privacy policy for Microsoft Store listing |
| `build/build-msix.ps1` | Store/sideload MSIX pack pipeline |
| `build/generate-msix-assets.ps1` | 100% scale Store logo and splash assets |

## Development Workflow

Always branch out in an isolated git worktree under `.worktrees/`, never on the main checkout:

```bash
git worktree add .worktrees/<branch-name> -b <branch-name>
```

1. Create the worktree and branch (command above)
2. Implement with tests in that worktree
3. Run all tests: `dotnet test`
4. Build release: `dotnet build -c Release`
5. Commit using `/commit`
6. After merge: `git worktree remove .worktrees/<branch-name>`

## Testing Requirements

- New features: Write unit tests
- Bug fixes: Write regression tests
- Before commit: All tests must pass

## Data Storage

- Settings: user-chosen `obfy.json` (`obfy config generate` / Save Configuration); UI prefs at `%APPDATA%\Obfy\ui-preferences.json` (override with `OBFY_UI_PREFERENCES`). A session `-o` path is not saved
- WebView2 profile: `%LOCALAPPDATA%\Obfy\WebView2\` (override with `OBFY_WEBVIEW2_USER_DATA`; the e2e tests give each launch its own)
- Logs: `%LOCALAPPDATA%\Obfy\Logs\`

## Code Reuse

| Utility | Purpose |
|---------|---------|
| `NameGenerator` | Generates obfuscated symbol names |
| `EncryptionHelper` | String encryption utilities |
| `PipelineContext` | Shared state across obfuscators |
| `IncrementalCache` | SHA-256 of Obfy version + input bytes + settings (`{output}.obfycache`); locked/corrupt cache is a miss |
