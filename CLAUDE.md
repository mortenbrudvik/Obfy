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
│   │   ├── Services/          # Theme sync, native file dialogs, shutdown coordination
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
│   ├── Obfy.VisualStudio/     # Visual Studio 2022 extension (C#); see "Visual Studio extension"
│   ├── Obfy.VSCode/           # VS Code stub (schema + problem matcher)
│   ├── Settings.Core/         # JSON settings persistence
│   └── Logging.Core/          # NLog logging infrastructure
├── Tests/
│   ├── Obfy.Tests/            # Core unit tests
│   ├── Obfy.Console.Tests/    # CLI parsing & integration tests
│   ├── Obfy.App.Tests/        # Session + handler unit tests (UI logic, no WPF)
│   ├── Obfy.Interop.Tests/    # Bridge, registry, events, request JSON + content contract tests
│   ├── Obfy.UI.Tests/         # Shell wiring, shutdown coordinator, host service
│   ├── Obfy.ScenarioTests/    # SDK fixtures, protected-app startup per target framework, installer settings + platform (Blazor/AOT/MAUI/Unity)
│   ├── Obfy.VisualStudio.Tests/ # VS helpers without a hive
│   └── Obfy.UI.AutomationTests/ # Locator tests + Playwright-over-WebView2 e2e + one FlaUI smoke (Category=UI)
├── scripts/
│   └── dev.ps1                # Dev loop: Vite dev server + the shell
├── docs/                      # Documentation
├── package/                   # MSIX manifest and Store assets
├── version.json               # The product version: the only place it is written
├── build/                     # Build scripts and installer
│   ├── build-installer.ps1    # Installer build: publish, self-protect, start checks, Inno Setup
│   ├── self-obfuscation.json  # Settings the installer protects Obfy's own assemblies with
│   ├── Get-ProductVersion.ps1 # Reads and validates version.json
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

# Build the installer (Inno Setup 6, the .NET SDK, Node 24). Protects Obfy's own assemblies, then starts the
# protected CLI and the protected desktop app and stops if either fails (needs an interactive Windows session and
# the WebView2 Runtime). See "Version and installer"
.\build\build-installer.ps1

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
- **Sessions** (`Obfy.App/Sessions`): `ProtectionSession` (settings, levels, the Custom snapshot, profile gates, config open/save), `InputSession` (files, solution/project expansion, include flags, output folder, symbol-map path, mode hint, drops), `LogBuffer` (run log, batched pushes), `ResultsStore` (summary, paged/searchable symbol index, preview decompile, exports), `RunCoordinator` (start/cancel, blockers with reasons, progress; nothing that touches the disk runs under its lock, which `IsRunning` and `EnsureNotRunning` wait on: `Start` reads and checks first, then confirms under the lock that the sessions' revisions did not move, and reads again a few times if they did; once the shell has approved a close (window, Quit or Restart), `ShutdownCoordinator` calls `TryRefuseNewRuns` and `Start` refuses with `run.blocked` "Obfy is closing.", and the run bar says the same; a close that does not complete reopens it. A logoff or a crash can still end the process under a run). They carry the rules the old ViewModels held; `Obfy.App.Tests` covers them
- **Screens** (`Src/WebApp/src/features`): a sidebar with **Files** (first-launch empty state, file table, output card with the symbol-map option), **Protection** (level cards, technique list and detail, scope), **Results** (Summary, Symbols, Preview and Log tabs; a running view while obfuscating) and **Guide** (F1: a short in-app quick-start, Getting started and Shortcuts, with a button that opens the full guide online), plus a persistent **run bar** (Obfuscate / Cancel, merge, blockers with reasons, progress). A successful run selects Results. Shortcuts: Ctrl+Enter obfuscate, F1 guide, add files, open/save configuration
- **Drag-and-drop**: The web area takes the OS drop (`AllowExternalDrop`), so the page shows its drop overlay from its own drag events and posts the dropped files to the host with `postMessageWithAdditionalObjects("files.dropped", files)`; `WebViewHandler` reads each `CoreWebView2File.Path` and the shell sends `DropPathsRequest`. The host refuses input during a run (`RunCoordinator.EnsureNotRunning`): a drop becomes a warning notice, and the picker, remove, clear, include and config-load calls fail with `run.blocked`. The window accepts `.dll`, `.exe`, `.cs`, `.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`, and `.json`. Solutions and projects expand into included outputs and skipped rows. A `.json` file loads as a configuration only when it is a JSON object with at least one Obfy settings key; `deps.json`, `runtimeconfig.json` and similar files are skipped with a notice
- **Closed-set**: Obfuscate runs a closed set when two or more included assemblies are listed, or when files came from a session (Merge still wins)
- **Empty output** (desktop app): One file writes `*.obfuscated.*` next to the input. Two or more `.cs` files compile together into `obfy-src` next to the first. A closed set writes `obfy-out` next to the first included assembly (for a solution, usually inside `bin`). Merge writes `*.obfuscated.*` next to the first input. With an output folder set, each file keeps its input name there. The run bar's hint and the run use one rule (`InputSession`). A run whose planned output (`OutputPlan.Outputs`) is a listed file is blocked with the reason on the run bar (`OutputCollisions`); so is a run whose symbol-map path (typed, or relative to the output folder) is a listed file, included or skipped, or one of those planned outputs. The CLI differs: `--merge` without `-o` writes `{name}.merged{ext}`, and a solution or project writes `obfy-out` next to the solution or project file (`docs/CLI.md`)
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
| Virtualization | 24 | Bytecode interpreter for eligible instance and static methods (objects, non-generic calls, fields, newobj, ldstr; i4/i8/r4/r8). Skips EH, generic methods/types/calls, byref, custom structs/`Nullable<T>`, switch, constructors, typeof (`ldtoken`), interpolators that allocate `DefaultInterpolatedStringHandler`, and `using` / enumerator-struct foreach (array foreach can be encoded). Per-build permute+XOR; gated off NativeAOT / IL2CPP / Blazor WASM; off in every preset. The copied interpreter is declared in full before its bodies are copied (`VmImporter`), so its own members are references to definitions and follow symbol renaming. Deterrent, not confidentiality. |
| MethodEncryption | 25 | XOR-encrypts method IL in the PE (Windows; per-method keys; skips generics). The in-assembly decryptor needs a mapped module; a packed app loads from a stream, so `MethodImageDecryptor` decrypts first (see Packing) |
| ControlFlow | 30 | State-machine dispatch of basic blocks and/or opaque predicates |
| ReferenceProxy | 40 | Hides call targets behind proxy methods. Skips constructors, generics, varargs, prefixed calls (`constrained.`, `tail.`, `readonly.`), pointers and unknown receivers. A known value-type receiver is passed by reference; `callvirt` on a value type stays direct. Out-of-module calls need `proxyExternalCalls`, and nested compiler/interop types are skipped |
| SymbolRenaming | 50 | Renames types, methods, fields, properties, events, namespaces. Never renames a virtual method. A snapshot/apply pass copies each new name onto the references that bind by name: MemberRefs (including those on generic instantiations of the module's own types and on a derived-type parent), custom attribute named arguments, `typeof` and enum arguments. The writer does not preserve MemberRef or MethodSpec row ids (`AssemblyProcessor.OutputMetadataFlags`), so the original names do not stay in unreferenced rows |
| MetadataRemoval | 90 | Strips debug info and attributes |

Packing (post-save, not an `IObfuscator`): native win-x64 FDD CLR-host stub that replaces the managed PE; `packing.rid: portable` keeps the managed launcher. Closed-set / solution runs pack each entry-point output. Not a Pre-JIT packer. Both hosts load the payload with `LoadFromStream`, which has no mapped module, so both run `MethodImageDecryptor.Decrypt` on the bytes first (`Obfy.PackedBootstrap/MethodImageDecryptor.cs`: called from `PackedBootstrap`, and compiled into the portable launcher from the `Obfy.MethodImageDecryptor.cs` resource in Obfy.Core). That file is an input of the native-host source stamp: after editing it or `PackedBootstrap.cs`, rebuild the host (`Src/Obfy.NativeHost/build.ps1`) and commit the exe and `dist/sources.sha256`, or CI fails.

**Target frameworks:** every injected framework type and member goes through `Obfy.Core/Utilities/FrameworkReferences` (scope by the module's core library and version; `SupportsProcessPath`, `SupportsTickCount64`, `SupportsRuntimeInformation` decide members), and a test (`FrameworkReferenceChokePointTests`) fails if an obfuscator builds its own reference. Protected apps run on netcoreapp3.1, net5.0 to net10.0 and net461 to net48, and netstandard2.0 libraries run on those runtimes; no technique is gated by target. `Obfy.ScenarioTests/ProtectedConsoleStartupTests` builds and runs an SDK app per target and skips a target visibly when its runtime or reference assemblies are not installed.

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
| `version.json`, `Directory.Build.props` | The product version, and the props file that gives it to the projects under `Src` |
| `build/build-installer.ps1`, `build/self-obfuscation.json` | Installer build (self-protection and start checks) and the settings it protects Obfy's own assemblies with |
| `Obfy.Core/Utilities/FrameworkReferences.cs` | The one place that scopes injected framework types and members to the target framework |
| `Obfy.Console/RunPathGuard.cs` | Refuses a `--map` / `--report` path that would replace an input, an output, the `-c` file or the signing key file |
| `Obfy.VisualStudio/Services/CliProcessRunner.cs` | Runs the CLI for the Visual Studio extension with a time limit, cancellation and a whole-tree kill |
| `package/AppxManifest.xml` | MSIX identity, full-trust capability, CLI alias (its Version is a snapshot pinned to `version.json`) |
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

## Version and installer

- **Version**: `version.json` holds exactly three integer keys, `major`, `minor` and `patch` (0 to 65535, no leading zeros, nothing else), and is the only place the product version is written. `Directory.Build.props` gives it to every project under `Src` as `Version` (assembly, file, informational and NuGet package version), except `Obfy.VisualStudio` (its version is in `source.extension.vsixmanifest`), `Obfy.PackedBootstrap` and `Obfy.VmRuntime`. `build-installer.ps1` reads it through `build/Get-ProductVersion.ps1` and passes `/DMyAppVersion` to ISCC (`ObfySetup.iss` has no default and stops with `#error` without it). `build-msix.ps1` stamps the package `Identity Version` from it. `publish.yml` runs `build/Resolve-PackageVersion.ps1 -VersionFile`, so a release tag (or the manual input) must carry the same `major.minor.patch`; a prerelease suffix on the tag is allowed, and `-p:Version=...` overrides the file for the build. The one copy kept by hand is the `Identity Version` snapshot in `package/AppxManifest.xml` (`major.minor.patch.0`): a test fails until it matches. A test also fails if a project or props file states a `<Version>`, so never add one
- **Installer build** (`build/build-installer.ps1`, seven steps: find Inno Setup, clean the publish folders, publish the CLI, publish the UI, protect and check, prepare the output folder and the WebView2 bootstrapper, compile the installer). Step 5 protects Obfy's own assemblies (`obfy.dll`, `ObfyUI.dll`, `Obfy.App`, `Obfy.Core`, `Obfy.Interop`, `Obfy.WebView`, `Obfy.Common`, `Settings.Core`, `Logging.Core`) with a copy of the freshly published `obfy.exe`, in **one closed-set run per publish folder** (`build/publish` for the CLI, `build/publish-ui` for the app), because the assemblies call each other: protecting them one at a time renames public types in Obfy.Core while the CLI still calls the old names (`InstallerSelfProtectionScenarioTests` runs both ways). Nothing is copied back until every output exists. The settings are the whole of `build/self-obfuscation.json`, passed with `-c` and no technique flags: string encryption, control flow (switch mode, intensity 50), symbol renaming (`preservePublicApi: false`, `preserveXaml: true`) and metadata removal, with `exclusions` for the names the shipped programs read at run time (`Obfy.Core.Models`, `Obfy.App.Models`, `*Request`, `AppInfo`, `ApiError`, `UiPreferences`, `<>*`). A name that a program reads and the renamer takes will fail the checks below; add it to `exclusions` there
- **Start checks** (still step 5, before the temp folders are removed and before any packaging). `Assert-ProcessedCli` runs the protected `obfy.exe`: `--version` prints a version; it builds a throw-away net10.0 console project under `%TEMP%`, protects it at `-l standard` (the output must report encrypted strings) and runs the result; `config generate` must write `"level"` and `"stringEncryption"`; and a handwritten configuration with every protection off must load without "Unknown configuration key", report "No transformations were applied", and give an output that runs. `Assert-ProcessedUi` starts the protected `ObfyUI.exe` on that probe with its own WebView2 profile and preferences (`OBFY_WEBVIEW2_USER_DATA`, `OBFY_UI_PREFERENCES`) and a debug port, then reads the page over the DevTools protocol: the Files screen is shown (not the crash view) and the bridge exists; `app.info`, `files.get` and `settings.applyLevel` answer with the documented names; a run started through `run.start` finishes with no failures within 120 seconds; and the assembly it wrote runs. It needs an interactive Windows session and the WebView2 Runtime, and when the shell is elevated it sets the machine policy `HKLM:\Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments` for `ObfyUI.exe` (an elevated WebView2 host ignores the environment variable, as in the CI UI-test step) and puts it back afterwards. It does not prove every screen, the native file dialog or a machine with no desktop
- **When a check fails** the script throws and no installer is built. The error carries the command's own output, and the UI failures end with the tail of the newest log in `%LOCALAPPDATA%\Obfy\Logs`. The protected files stay in `build/publish` and `build/publish-ui`, so run `build\publish\obfy.exe` or `build\publish-ui\ObfyUI.exe` by hand; `build/temp-obfy`, `obfuscated-cli` and `obfuscated-ui` are removed by the next run. `-SkipObfuscation` and `-SkipPublish` skip protection and the checks. `.github/workflows/package.yml` runs the script on the Windows runner

## Visual Studio extension

- **Runs**: `ObfuscateCommand` and the post-build step (`BuildEvents`) register each run with `ObfuscationRunTracker` and pass its token to `CliProcessRunner`, which owns the CLI process. A run ends when the process exits, the token is cancelled or the time limit passes (30 minutes, `CliProcessRunner.DefaultTimeoutMinutes`; Tools > Options > Obfy > General > Run Timeout (minutes), above 1440 counts as 1440, below 1 as 30). On cancel or timeout the whole process tree is ended: the CLI runs in a kill-on-close job object (`ProcessJob`), with `taskkill /T /F` as the fallback. The tracker cancels a run when the solution closes, the package is disposed, a new build of the same project starts, or the **Cancel Obfuscation** command is used (project context menu, visible only while a run is active)
- **Commands**: register each one as `[Command(PackageGuids.CommandSetString, PackageIds.x)]`. The one-argument form registers under the package GUID, and the `.vsct` defines every button under the command-set GUID, so the command would never run. `CommandTableTests` reads the `.vsct` and the command sources and fails when they disagree
- **Tests** (`Obfy.VisualStudio.Tests`) link the files of `Src/Obfy.VisualStudio` that have no Visual Studio dependency (see the test project); a new such file that needs a test must be linked there. The tests that run real child processes are Windows-only
- **Dependencies**: `MessagePack` 2.5.303 is a direct reference (version in `Directory.Packages.props`) that replaces the vulnerable 2.5.192 the Visual Studio SDK brings in; remove it when the 17.x SDK asks for 2.5.303 or later. It is not in the VSIX

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
| `IncrementalCache` | `{output}.obfycache`: a JSON entry with a SHA-256 key (Obfy.Core version + MVID, input bytes, settings, signing key file, embedded dependencies), the cached run's statistics, three counts (symbol-map entries, skipped items, warnings) and the length + SHA-256 of the output and of its packing files. Numbers and hashes only, never a name or the symbol map. A hit needs the key and every fingerprint to match, so an output replaced since the cache was written is a miss; a locked, corrupt or older-layout file is a miss. It covers a single assembly or a merge, and is not used when the output replaces the input. `ObfuscationRunOptions` (`NeedsSymbolMap`, `NeedsReport`) makes a hit decline when the caller needs what the cache does not hold: `--map` and `--report` set them, the desktop app sets both. The entry is written to a temp file and moved into place |
