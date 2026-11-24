# Changelog

All notable changes to Obfy will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Native `.json` configuration drag-and-drop confirmation dialog when protection settings have unsaved changes (`settings.confirmConfigDrop` / `settings.cancelConfigDrop`)
- `global.json`, `nuget.config` with `packageSourceMapping`, NuGet/npm Dependabot ecosystems (`.github/dependabot.yml`), and a `macos-cli` CI job (`Obfy.Console.Tests`)
- End-to-end scenario tests for `PublishSingleFile=true` (with `AntiTamper`), `PublishTrimmed=true` (with injected `FrameworkReferences`), and separate vs. closed-set sequential short names (`Tests/Obfy.ScenarioTests/PublishModesScenarioTests.cs`)
- Installer build metadata check (`Assert-ProtectedUiXamlBindings` in `build/build-installer.ps1`) verifying all 7 `ShellWindow.xaml` failure-panel bindings survive self-obfuscation in `ObfyUI.dll`
- `app.reportError` bridge endpoint (`IpcNames.AppReportError`, `ReportPageErrorRequestHandler`) and `ErrorBoundary` integration: unhandled screen render errors send sanitized `screen` and `errorType` identifiers (up to 64 ASCII characters; never messages, stacks, or paths) to the host log
- "Save As…" button and `Ctrl+Shift+S` shortcut on the Protection screen (`api.settings.saveConfig(saveAs: true)`), plus an accessible in-app `ConfirmDialog` before replacing unsaved (`dirty`) settings via `Open…`, `Load a configuration…`, or `Ctrl+L`
- Unknown-key warning notices on the desktop `FilesPage` and `ProtectionPage` backed by the shared `Obfy.Core.Utilities.ConfigKeyValidator` when opening or dropping a configuration with unrecognized JSON keys
- Cross-platform RSA-2048 `.snk` signing, output assembly network-reference scan, VM + symbol renaming merge, self-reflection renaming, and Roslyn `nameof(...)` tests in `Tests/Obfy.Tests` (`ReviewFixesWave2CoreTests`, `VmWithRenamingTests`, `ObfySchemaContractTests`, `CentralPackageVersionTests`)
- `app.openGuide` bridge endpoint opens the online guide (https://brudvik.com/obfy/guide) in the default browser. The page sends a section id (`getting-started`, `files-and-output`, `levels-and-settings`, `techniques`, `what-can-break`, `results`, `stack-traces`, `shortcuts`, `command-line`, `ide-and-build`, `troubleshooting`); the host builds the `https` URL from the `GuideLinks` allowlist, and an unknown or missing id opens the guide's start. `IShellLauncher.OpenUrl` accepts `https` only. The page never supplies a URL
- The Guide screen has an "Open the full guide online" button, and a technique's "Read more" link is now "Read more online" and opens its section of the online guide in the browser

### Changed
- `AntiTamper` is automatically disabled with a report warning when `runtimeProfile` is `NativeAot`, `UnityIl2Cpp`, or `BlazorWasm` (`RuntimeProfileGating`, `schemas/obfy.schema.json`, `NativeAotTests`, and docs), mirrored in the desktop UI `ProfileGates` (`ProtectionSession`)
- Expanded `docs/Advanced.md` performance guidance across all 16 obfuscation techniques and added empirical native-packed lifecycle E2E tests (`Tests/Obfy.Tests/ReviewFixesWave4PackedTests.cs`) covering `GetEntryAssembly() == null`, `Main` exception handling (`ExitCode == 1` with `stderr` stack trace vs. secondary-thread `AppDomain.UnhandledException`), and joined vs. unjoined foreground threads
- Tightened `schemas/obfy.schema.json` with `additionalProperties: false` on root and section objects, `$comment` properties, nullable `signing.keyFile` and `signing.passwordEnvironmentVariable` fields, and case-insensitive enum values for `controlFlow.mode`, `symbolRenaming.mode`, `runtimeProfile`, and `encryptionAlgorithm`
- Clicking "Open the full guide online" on the Guide screen now passes the active topic's `GuideLinks` section id (such as `shortcuts` for the Shortcuts topic) so the browser opens the matching online guide section
- Updated `PRIVACY.md`, `CLAUDE.md`, `README.md`, `docs/Advanced.md`, `docs/CLI.md`, `docs/Platforms.md`, `docs/Techniques.md`, `docs/Competitive-Analysis.md`, `docs/Roadmap.md`, `docs/Testing-Roadmap.md`, `examples/maui/obfy.json`, `docs/review-progress/progress.json`, and `backlog.html` for Wave 2 fixes and scrubbed diagnostic logging
- The in-app Guide is a short quick-start (Getting started and Shortcuts). The full guide moved online. Its text now says that Results opens only when every file succeeds, that the symbol map file is written only when Write a symbol map is on (it is off by default), that Preview needs a chosen assembly, and that results are not kept after the next run. The Ctrl+C row is gone (there is no handler), and each shortcut says when it works
- Corrected technique copy on the Protection screen: Hash names differ from build to build (the hash is salted per run), type patterns match the short type name only, Preserve XAML keeps public properties of bindable-looking types but not type names, Remove attributes drops nine named compiler and debugger attributes and keeps your own, Merge needs every included file to be an assembly, resource encryption rewrites only `GetManifestResourceStream` calls, and a packed native launcher is not strong-name signed
- The desktop app is a WPF shell that hosts a WebView2 page built with React. A sidebar has Files, Protection, Results, and Guide, and a run bar along the bottom starts and cancels a run. The output row (folder, Browse, symbol-map toggle and path) is on the Files screen. A successful run opens Results. Preview decompiles only the assembly you select
- Levels are cards: Minimal, Standard, Aggressive, or Custom. Choosing Custom again restores the technique switches from before the preset
- The Log tab has Problems only, Timestamps, and Wrap options and Copy and Clear buttons. A new run keeps the earlier log and inserts a separator when that log is not empty
- The overall progress bar is indeterminate for one included file, a merge, and a closed set
- Symbol renaming never renames a virtual method: overrides, interface implementations, finalizers and delegate `Invoke` keep their names, in every type. This keeps dispatch through a base type or interface working, at the cost of leaving those member names readable
- Obfy refuses an input that already contains its own runtime helpers (`Obfy.Runtime.*`), with a message, instead of protecting it a second time and producing output that returns wrong values
- A protection that cannot apply to the input (for example AntiDebug on source code) now produces one warning that names the skipped techniques, and a run that changed nothing says so
- `obfy config generate` refuses to overwrite an existing file unless `--force` is given, and creates a missing output folder
- A closed set applies the chosen level and validates its settings, as a single file does
- The Preview tab waits up to five minutes for a large assembly and the Log tab keeps the latest 5,000 lines in the page (the host keeps the full log)
- A source-code run honors the `inclusions` and `exclusions` settings and the default exclusion attributes such as `[Serializable]`, as an assembly run does
- `version.json` is the only place the product version is written. Every project under `Src` takes its assembly, file, informational and NuGet package version from it through `Directory.Build.props`, except `Obfy.PackedBootstrap` and `Obfy.VmRuntime`. `build-installer.ps1` reads it and passes `/DMyAppVersion` to Inno Setup, and `ObfySetup.iss` has no default and stops with `#error` when it is not given one. `build-msix.ps1` stamps the package identity from it. The hand-typed `<Version>` lines in the CLI, Core and UI projects are gone. `package/AppxManifest.xml` keeps a snapshot of the version that a test pins to `version.json`, so bumping one without the other fails the tests. `-p:Version=...` on the command line still wins
- A release tag, or the version typed into a manual run of `publish.yml`, must carry the same `major.minor.patch` as `version.json`. The workflow checks this right after checkout and fails with a message that says what to do when it does not; a prerelease suffix such as `2.1.0-beta.1` is still allowed on the tag. `version.json` must be exactly the three integer keys `major`, `minor` and `patch`, each 0 to 65535 without leading zeros; any other content fails the build with a message that names the file

### Removed
- The Visual Studio 2022 extension (`Src/Obfy.VisualStudio`, a VSIX with the Obfuscate, Enable Post-Build Obfuscation, Obfy Settings and Cancel Obfuscation commands, a Tools > Options > Obfy page and a post-build step) was removed, with its test project `Tests/Obfy.VisualStudio.Tests` and the packages only it used (`Community.VisualStudio.Toolkit.17`, `Microsoft.VisualStudio.SDK`, `Microsoft.VisualStudio.SDK.Analyzers`, `Microsoft.VSSDK.BuildTools`, the `MessagePack` pin and `System.Text.Json`). It was only ever built from source, never published to the Marketplace or attached to a release. The command-line tool and the desktop app are the supported ways to run Obfy. Visual Studio solution and project files (`.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`) are still accepted as input. `postBuildEnabled` in `obfy.json` is still accepted, but nothing reads it
- The JetBrains Rider plugin (`Src/Obfy.Rider`) was removed, with its Gradle build, JVM tests, and the `rider-tests` CI job. It was never published to a marketplace or attached to a release

### Fixed
- `DotnetToolPackTests` installs the packed tool from its temp folder with a local `--configfile` so repo-root `nuget.config` `packageSourceMapping` stays intact on Windows, Linux, and macOS
- `ScenarioHarness` strips inherited MSBuild/SDK-resolver environment variables, propagates repo `global.json` into temp fixtures, disables shared compiler servers, and bounds stream drain so `PublishSingleFile` and `PublishTrimmed` scenario tests do not hit SDK skew or task-host hangs in CI
- **Open log folder** resolves the redirected MSIX package folder (`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Obfy\Logs`) via `LoggingConfiguration.ResolveExplorerLogDirectory()` (`GetCurrentPackageFamilyName`) when running as a Store/MSIX app, with Store redirection paths documented in `PRIVACY.md`
- Upgraded `actions/setup-node` to verified Node 24 `v6.5.0` (`249970729cb0ef3589644e2896645e5dc5ba9c38`) across all four GitHub Actions workflows (`.github/workflows/ci.yml`, `package.yml`, `platform.yml`, and `publish.yml`)
- Native packed host (`Obfy.PackedBootstrap` / `Obfy.NativeHost`): `[STAThread]` apartment dispatch, traversal-safe `ResolvingUnmanagedDll` sibling/native probing, heap-allocated path buffers in `host.c` (freeing 320 KB of stack before `run()`), consistent `GetModuleFileNameW` truncation checks, SHA-256 verification of all 4 `8.0.6` native host pack files, and BOM-free UTF-8 `rc.exe /c 65001` resource generation
- CI workflows updated to verified Node 24 action SHAs (`checkout`, `setup-dotnet`, `upload-artifact`, `sticky-pull-request-comment`), job `timeout-minutes`, and `Check Coverage Threshold` running only on `if: success()`
- Portable managed launchers (`ManagedLauncherPacker` / `PackingApplicator`) are strong-name signed after packing when strong-name signing is enabled, invoke `[STAThread]` entry points on an STA thread on Windows, print full exception details on host failure, and scrub file paths and exception messages from packer `Trace.WriteLine` messages (`NativePacker` and `ManagedLauncherPacker`)
- Core pipeline, caching, VM, PE encryption, and reporting fixes: `MergeAndObfuscateAsync` supports incremental caching via `IncrementalCache.ComputeMergeKey` (keyed by inputs, filenames, settings, signing key, and embedded sibling dependencies) and probes original input directories plus `AssemblyMerge.SearchDirectories` for sibling dependency embedding (`DependencyEmbeddingObfuscator`); `ObfySettings.Validate` and `ApplyLevel` guard null sections and initialize null lists; `ObfuscateBatchAsync` compiles non-contiguous `.cs` inputs together while preserving result order; `ReportService` and `HtmlReportGenerator` populate all `SettingsSummary` technique flags, format durations over 1 hour, and handle `long.MinValue` byte sizes; `VmEncoder` rejects `Code.Volatile` and expands BCL byref-like (`IsByRefLike`) type guards; and `MethodBodyPeEncryptor` validates PE section/fat-header/RVA bounds and rejects duplicate method-encryption magic markers
- Desktop app, CLI, interop, and WPF shell fixes: bounded host `LogBuffer` (`5,000` lines) and frontend `logModel` (`MAX_LOG_LINES`) evict older `Info`/`Debug`/`Success` lines first to preserve `Warning`/`Error` lines; async file/folder/key/config pickers revalidate `RunCoordinator.WhileIdle` post-await alongside `PickSymbolMap`/`ExportMap`/`UpdateSettings` guards; CLI `CreateRootCommand` uses local `RootOptions` instead of shared static options; `OutputCollisions` detects NTFS alternate data streams (`\\?\UNC\` and drive-relative colons); `RunCoordinator.FindModuleResult` and `FindLoadFailure` restrict filename fallback to unique filenames; `SettingsService` serializes preference writes using a `SemaphoreSlim` and unique temporary files; `ResultsStore.LoadKnownPreviewAsync` validates preview paths atomically; `ShutdownCoordinator` guards concurrent restart/quit races and `IsLoaded` window ownership; `ThemeController` wraps `DefaultBackgroundColor` and handles `ObjectDisposedException`; and `Obfy.UI.csproj` cleans `$(TargetDir)Assets` in `CopyFrontend` and blocks publishing with `-p:SkipFrontendBuild=true` unless `-p:AllowStaleFrontendPublish=true` is passed
- Frontend reliability and accessibility fixes: `useActionRunner` clears stale action errors across state/screen changes and ignores superseded async failures; `OptionControl` (`useNumberSync`) keeps numeric drafts synced with host snapshots without snapping back while typing; `LevelCards` and `FileTable` add roving `tabIndex`, arrow-key/Enter/Space keyboard navigation, and focus restoration; `Meter` and `Field` fix HTML nesting and `aria-describedby` semantics; and `useShortcuts` suppresses global shortcuts while an `alertdialog` is open
- Resource encryption no longer leaves an evaluation-stack imbalance in its typed resource loader and matches `System.Reflection.Assembly`/`RuntimeAssembly` and `System.Reflection.Module`/`RuntimeModule` signatures explicitly; string encryption matches `System.Resources.ResourceManager.GetString` signatures explicitly; and `DecryptorIl` disposes `Aes` in a `try`/`finally` block
- Control flow obfuscation and flattening fix opaque-predicate branch simplification (`SimplifyBranches`/`OptimizeBranches`) and propagate CFG stack depth for `MaxStack` sizing so zero-depth predicate sites and flattened switch CFGs remain verifiable
- Anti-tamper guards an empty CLR `DataDirectory` when hashing `Environment.ProcessPath` on single-file bundles and disposes `SHA256` in `try`/`finally`; anti-dump emits `xor eax, eax; ret` on x64 and `xor eax, eax; ret 0x1C` on x86 while skipping ARM64, loads `dbghelp.dll` via `LoadLibraryExW` (`LOAD_LIBRARY_SEARCH_SYSTEM32`), uses `UIntPtr` for the `VirtualProtect` size, and restores page protection
- `WildcardMatcher` uses `RegexOptions.NonBacktracking` with a 4,096-entry cache limit; `[Obfuscation]` rules normalize feature names case-insensitively and walk enclosing declaring types; symbol renaming collects parameter-renaming targets before renaming types or methods and preserves `NestedPublic`/protected APIs and non-renamed namespaces; reference proxies check enclosing type visibility and skip caller-sensitive BCL APIs; metadata removal covers assembly attributes; `FrameworkReferences` sets the Microsoft public key token for `System.Private.CoreLib` facades; and `SettingsValidator` validates nested objects recursively with cycle and indexer guards
- Virtualization widens mixed `R4`/`R8` float operands and coerces float slots across `BoxReturn`, binary math, comparisons, `CallVm`, `DoRet`, `SetLoc` and `SetArg`; screens out `IntPtr`, `UIntPtr` and `VarArg` calls/signatures in `VmEncoder` while allowing trailing `Br` instructions; and keys `VmMemberTables` entries by assembly or module scope
- Closed-set processing fixes a `HasEntryPoint` use-after-dispose when packing is enabled, traces entry-point reachability transitively via BFS across project and assembly references, rewrites `InternalsVisibleTo(..., PublicKey=...)` during closed-set signing, preserves `InputPath` in `MergeAndObfuscateAsync` and trims trailing directory separators in `ObfuscateBatchAsync`, deduplicates warnings and symbol summaries with aggregated sizes in combined reports, and HTML-encodes version and obfuscator names in HTML reports
- Roslyn source obfuscators decode raw interpolated string escapes (`{{`, `\u007b`, `\x7b`) in `SourceStringEncryptor`; avoid breaking pattern variables, nested labels, tick variable uniqueness and `CS0162` reachability in `SourceControlFlowObfuscator`; and preserve record positional properties, duck-typed C# pattern members and partial/override parameter alignment in `SourceSymbolRenamer`
- The CLI fills `null` JSON settings sections and lists with defaults (`FillNullSections`), detects duplicate keys via `JsonDocument`, prints `"--level is ignored"` and config-error warnings once (routing errors to `stderr`), applies wizard use-case defaults before displaying the summary, honors explicit `PreservePublicApiChoice` selections, and handles empty or invalid paths in the overwrite prompt loop
- The desktop app closes mutation-vs-run-start races with `RunCoordinator.WhileIdle` and atomic `InputSession.SetSymbolMap`, moves C# multi-file output planning off `InputSession._gate`, guards `ShellWindow` reveal and timer state when cloaking is refused or the window closes, wires `ILogger<WebViewControl>` via DI and flushes NLog on exit, displays notices in the empty `FilesPage` state, scrolls `RunningView` when new log lines arrive at the 200-line cap, updates the type-inclusion help text in `techniques.json`, and disables `ProtectionPage` controls plus `Ctrl+O`/`Ctrl+L` shortcuts while a run is active
- Installer and release scripts qualify `{sys}\taskkill.exe` in `build/ObfySetup.iss` (`PrepareToInstall` and `[UninstallRun]`) and guard `EnvAddPath` with `RegValueExists`, disable `preserveXaml` for the CLI folder during self-obfuscation and harden `CloseAsync`/HKLM policy cleanup in `build/build-installer.ps1`, delegate MSIX versioning in `build/build-msix.ps1` to `Get-ProductVersion.ps1`, and pass `NUGET_API_KEY` via `env:` instead of PowerShell string interpolation in `.github/workflows/publish.yml`
- A closed set with `preserveXaml` on no longer prints, against every assembly, that no ViewModel, View or DependencyObject was detected. That warning was raised for each assembly that had none and then copied onto every output, so the desktop app's `ShellViewModel` was warned about the libraries beside it. It is now raised once, and only when the whole set has no such type, and the text says it is about the closed set. A single assembly with `preserveXaml` on and no such type still gets the original warning
- Reference proxy no longer aborts the run when a call has five or more arguments. Aggressive failed on the first such call with "Opcode does not have an int32 operand" and wrote nothing. A static method with 5 to 16 parameters, or an instance method with 4 to 16, is proxied; more than 16 parameters stays a direct call, as before
- Reference proxy no longer crashes when the call target is a method on a private, protected, or private-protected nested type. The trampoline was emitted next to the type, and the runtime then rejected the call (`MethodAccessException`). The trampoline is now nested inside that type. On an open generic type the call stays direct
- Reference proxy no longer writes an assembly that crashes on an out-of-module instance call whose receiver is a value type the pass cannot see. `--proxy-external` on an async method that awaits `Task.Yield` threw `InvalidProgramException` inside the renamed `CountAsync`, with or without symbol renaming, because the nested `YieldAwaiter` was trampolined by value. A known value-type receiver is now passed by reference. An unknown receiver, and `callvirt` on a value type, stays a direct call. Nested compiler and interop types are skipped with the rest of those namespaces
- The installer's self-obfuscation keeps the names the shipped programs still read. A handwritten configuration is honored again (`level`, `stringEncryption`, and the rest of the documented keys), and the desktop app's window, bridge and Protection screen work after the installer build. The build runs the protected CLI and the protected desktop app before it packages anything, and stops if either does not start
- The installer protects each publish folder in one closed-set run, so renaming no longer leaves the shipped CLI calling types that were renamed in Obfy.Core. The shell assembly included in the UI folder is `ObfyUI.dll`
- A packed app whose method bodies were encrypted now starts. Both the native host and the portable launcher decrypt those bodies before loading the payload from a stream, because that load has no mapped module for the in-assembly decryptor to patch
- Starting a run no longer holds the run lock while it asks the file system about the listed files. With an input on an unreachable network share, the lock that `IsRunning`, `EnsureNotRunning` and the window's close handler wait on was held until the share timed out, so closing the window and every call that checks whether a run is going stalled with it. The files and settings are read and checked first, and a change made while that goes on makes Start read them again, so a run still protects exactly what was listed when it began and the collision check is about exactly that list. Once a close has been approved, a run can no longer start
- A run is refused when the symbol map's location is one of the listed files (included or skipped) or a file the run itself writes. Before, typing the path of a listed assembly as the symbol-map location replaced that assembly with JSON after a successful renaming run. The run bar says which file would be replaced and to choose another location for the map
- A custom attribute's named arguments, and the type names inside `typeof` and enum arguments (including arrays of them), are rewritten when the member or type they name is renamed. An attribute class in the same assembly applied as `[Info(Note = "x")]` threw `CustomAttributeFormatException` when the attribute was read after renaming, at every level, and the same happened for a field, an enum-typed property, or a named member declared on a base attribute. A member that is excluded, or kept because the public API is preserved, keeps its name in the argument
- A member reference whose parent is a derived type is rewritten to the member the runtime calls. The member is found on that type or up the base chain, with the base type's generic arguments substituted. When that substitution makes several methods share a signature (`Pick(T)` and `Pick(int)` on `Base<int>`), a virtual match is preferred and otherwise the last method in metadata order is used. Taking the first match renamed the reference onto the other method, so the call ran that method instead
- Obfuscated output no longer keeps, in its raw bytes, a name or a literal that renaming or encryption replaced. The assembly was written with the original string, user-string and blob heaps preserved, and the original MemberRef and MethodSpec rows were kept as well, so a renamed member used through a generic instantiation stayed readable next to its new name. That includes a generic method called with an open type argument (`Box<T>.Convert<U>` from a generic method). Names the renamer keeps are still in the bytes: virtual methods, a preserved public API, framework members, and the parameter names of constructors and accessors. String and constant literals stay unless that encryption is on
- An app that uses a member of a closed generic type it defines itself (`Box<int>.Item`, a method, property, event, closure, iterator or record on it) no longer dies with `MissingFieldException: Field not found: '<renamed>.Item'` after symbol renaming with `preservePublicApi` off, the default. C# refers to such a member through a member reference on the instantiated type, which the CLR binds by name, and only closed-set runs rewrote those references. Single-assembly and merge runs rewrite them now. In a closed set, an access to a multidimensional array whose element type has its own `Get(int, int)` (`Cell[,]` and `Cell.Get(int, int)`) no longer calls the renamed name and fails with `MissingMethodException`
- An app protected with Virtualization and symbol renaming together (`preservePublicApi` off, the default) no longer fails inside the interpreter with `MissingFieldException: Field not found: '<renamed>.Type'`. The copied interpreter referred to some of its own fields by name, so renaming left the references stale; they now refer to the definitions and follow the rename. Virtualization alone was not affected
- Protected apps no longer crash at startup, or silently lose a protection, on targets other than net7.0 to net10.0. Injected framework types and members are now scoped to the target framework's own assemblies. On .NET Framework and netstandard, AntiTamper no longer references the .NET 6 `Environment.ProcessPath`, which killed an Aggressive app on net48 in its module initializer. On net5 and net6, AntiTamper and every AES variant no longer name `System.Security.Cryptography`, which does not exist before .NET 7 (`FileNotFoundException`). On net6 and older, AntiDump now patches `MiniDumpWriteDump`; the `RuntimeInformation` type it reads the architecture through is not in the `System.Runtime` facade there, so it never patched it, and the failure was swallowed. On net5 and on netcoreapp3.1, types missing from the `System.Runtime` facade (`File` on net5; `Environment`, `Debugger`, `File`, `BitConverter`, `AppDomain`, `ResolveEventHandler`, `MemoryStream`, `ResourceManager`, `Convert` and `RuntimeInformation` on netcoreapp3.1) are referenced through the assemblies that carry them. The target framework turns no technique off, except native packing, which needs a net8.0 or later payload. The startup tests build and run an SDK app on net10.0 to net5.0, netcoreapp3.1, net48 and net461, each where its runtime (and, for net48 and net461, its reference assemblies) is installed. They run no other .NET Framework version and no netstandard2.0 library; the references of a protected netstandard2.0 library are only checked against the netcoreapp3.1, net6.0, net10.0 and net48 runtimes. CI installs the .NET 9 SDK so the net9.0 cases always run
- Ctrl+C now stops a single-assembly or merge run in the CLI. Both ran to completion and wrote their output after Ctrl+C was pressed; closed-set and C# source runs already stopped
- A closed-set run that fails without naming a file (an assembly that is already protected, an output path collision, a signing key that cannot be loaded, invalid settings), "No built outputs found", "No assemblies found.", two solution files, and `--merge` with fewer than two assemblies each print one problem-matcher line (`path(1,1): error: message`) next to the red text, filed against the solution or project file or against the first input. Before, a failing `obfy MyApp.sln` task showed nothing in the Problems panel. A failure that has its own line per file (a load failure, a missing file) gets no second line
- `--map` and `--report` are refused when the path would replace a file the run needs. Before, `obfy app.dll -o out --map app.dll` replaced the input with JSON and `--map out\app.dll` replaced the protected output. The refused paths are every input (a solution's project files and built outputs included), each output with the packing files beside it (and, for a single assembly or a merge, its `{output}.obfycache` file), the `-c` file, the signing key file named in it (`signing.keyFile`), with packing the `.runtimeconfig.json` beside an input assembly, the `--map` file for `--report`, and any path with an alternate data stream (`App.dll::$DATA`). The CLI prints which option and which file, adds a problem-matcher line, and exits 1, in a dry run too. Paths are compared as files: letter case, `..` and links do not hide a match. Hard links, `subst` and mapped drives, and sibling DLLs that an input names but the command line does not list are not covered (`docs/CLI.md`)
- `--incremental` returned empty results on a cache hit and could vouch for a file it did not write. The CLI table showed no numbers, `--map` wrote nothing, `--report` summarised zero symbols and the desktop Results screen was empty, all with a success message. A hit now returns the statistics of the run that wrote the output and says so (`Incremental: reused cached output`). The cache file is JSON with the key, those statistics, three counts (symbol-map entries, skipped items, warnings) and the length and SHA-256 of the output and of the packing files beside it; it never holds a name, a path or the symbol map. A hit is taken only when those files still match, so an output replaced since (a build that copied the unprotected original over it, a signing step) is rebuilt and never reported as reused. `--map` rebuilds when the earlier run renamed symbols, `--report` also when it skipped items or raised warnings, and the desktop app, which needs both, rebuilds in all three cases. A cache file written by an earlier build is a miss and is rewritten
- `--incremental` no longer prints a false "Incremental cache not written: the input or one of its dependencies changed while the run was in progress" when the output replaces the input (`obfy app.dll -o <folder of app.dll>`, the MSBuild post-build step). That run now neither reads nor writes the cache and says so once. A closed set (several assemblies, a solution or a project) and C# source run in full with one warning that `--incremental` does not cover them; before they ignored it silently. The cache entry is written to a temporary file and moved into place, so a crash or two runs on the same output cannot leave a truncated entry
- A page error on one screen of the desktop app no longer takes the whole window with it. The sidebar, run bar and shortcuts stay, a running run can still be cancelled, and the content area shows "The Protection screen could not be shown" (or the screen's name) with Try again, which moves focus to the content area. A throw before the shell has mounted now reveals the window on the crash view instead of after the 8-second wait
- Text typed in the output folder and symbol-map boxes and the text and list options of the desktop app is no longer overwritten by the host's reply to an earlier commit: `C:\out`, Enter and `2` ended as `C:\out`. Text typed after a commit survives the echo of that commit; when the host answers with another value and nothing was typed since, the box shows the host's value; a refused write keeps the typed text; and a configuration loaded afterwards replaces it
- A screen reader announces why Obfuscate is disabled: the reason is in a status region that is on the run bar in every state. A "Could not cancel the run" message now stays until the run's status changes instead of vanishing with the next progress update, and is dropped when it arrives after the run it was about has ended
- A failure while writing, post-processing or signing an obfuscated assembly no longer truncates or deletes the existing output, including when the output is the input itself (the documented MSBuild post-build step)
- Symbol renaming no longer breaks `ToString`, `Equals` and `GetHashCode` overrides in structs and sealed classes, delegate types, and finalizers, which dispatched to the base implementation or failed to load
- Reference proxies no longer wrap `__arglist` (vararg) calls inside the module, which produced invalid IL
- The window appears as soon as the page has drawn, instead of after an 8-second wait at every launch
- Closing Obfy, or choosing Quit or Restart, during a run asks first, cancels the run and waits for it to stop instead of killing it mid-write. Restart saves preferences before the new instance starts
- If the page cannot load (missing files, a failed or HTTP-error load) Obfy shows the Restart / Open log folder / Quit panel instead of a blank window. Renderer crashes that recovered no longer add up to the permanent failure panel, and a broken WebView2 loader at startup shows an error and exits instead of leaving an invisible process
- Crash details are flushed to the log before the process ends, and the failure panel says so when the log folder could not be created. Startup files, `-o` and the configuration are no longer dropped when the saved preferences cannot be read, and failed drops show a notice. The WebView refuses permissions, downloads, script dialogs and pinch zoom
- Logs no longer contain input, output, key-file, report or profile paths, or exception messages, in the engine, the bridge and the window host: they carry counts and exception types
- Run progress can no longer jump back to "Cancelling" after a run has finished, and the run bar, results and settings snapshots arrive in order
- Failed runs leave a trace in the log (exception type only). A missing or unwritable symbol map shows a warning instead of a plain "Obfuscation complete"
- Closed-set runs count an assembly with no result as failed, previews work for `.DLL` and `.EXE` output, and a run protects the files listed when it started. Output and protection settings cannot be changed while a run is in progress
- Saving a configuration keeps its `$schema` line and unknown fields and is atomic. A configuration with a null section no longer breaks the settings screen. A preset file whose flags differ from its level loads as Custom
- Dropping `deps.json`, `runtimeconfig.json` or any other `.json` without Obfy settings no longer loads as a configuration or becomes the file Save overwrites. Clearing the symbol-map path resets it to the default instead of showing an error
- A saved output folder on an unreachable network share can no longer freeze startup, and `\\.\` and `\\?\` device paths are refused as output locations. Dropped files that are unsupported, missing or already listed say so instead of vanishing
- `--output=dir` and `-o=dir` are honored at startup, and `-o` without a value no longer misparses
- Obfuscate (Ctrl+Enter) and Save send an output folder or symbol-map path you typed but have not left yet. Esc closes a menu or clears a search box without also cancelling the run. A late or out-of-order update can no longer leave the run bar on "Running" or an old error on screen, and fast edits to number boxes and sliders are applied in order
- The incremental cache hits for NativeAOT, Unity and Blazor profiles, is invalidated by Obfy hotfixes, is dropped before the output is overwritten, reports a failed write, and is not written when the input changed mid-run
- Embedding dependencies, and the cache key, only use plain `{Name}.dll` files from the input folder, so a hostile assembly reference name cannot pull in a DLL from outside it
- Closed-set runs report failures instead of throwing, leave no `.obfyprev` files after a failed commit, report inputs left out by exclude patterns, and update the public key tokens in references between members when signing
- Running Obfy over its own output no longer double-encrypts resource strings or resources; they are left as they are, with a warning. A resource whose strings could not be encrypted is reported as not protected, not as "excluded by pattern"
- AntiDebug no longer emits invalid IL when a method begins with `volatile.`, `unaligned.`, `constrained.` or `tail.`
- Native packing validates the payload's target framework before changing any file, keeps the input's runtime configuration (additional frameworks, configProperties), restores the original if the runtime configuration cannot be written, and a packed app with a `void` or `Task` `Main` reports its real exit code instead of always 0. Encrypting method bodies no longer scales quadratically with method count
- Virtualization no longer inverts NaN handling in float `<=` and `>=` comparisons, skips methods that use `Nullable<T>`, lone-surrogate string literals, or APIs taking or returning spans, byrefs or pointers, and reads unassigned long, float, double and reference locals as zero. A missing dependency breaks only the virtualized methods that use it, a large assembly no longer fails the whole pass, and runaway recursion throws `InsufficientExecutionStackException`
- Source string encryption no longer changes the meaning of interpolated strings, writes one file for one input file, and no longer duplicates its helper on re-runs. Source control flow no longer hangs on 900+ statement methods, breaks `out` parameters, throws or overflows in its predicates, transforms methods at intensity 0, or writes unparseable text. Source symbol renaming keeps interface events, anonymous and tuple member names and partial methods consistent, avoids names that collide with existing identifiers, and keeps symbol-map keys unique. Source runs warn when the output has compile errors the input did not
- A path or file name containing square brackets (such as `App [Release]`) no longer makes a finished run exit with code 1 and skip the map and report. `-c` files are read case-insensitively and unknown keys print a warning. `assemblyMerge.enabled: true` merges, and `--internalize` overrides the config only when typed. `config wizard` writes enums as names so the file validates against the schema, and `-l` rejects numbers that are not a level
- The installer keeps the system Path expandable, notifies running programs, and on uninstall removes only Obfy's own entry. It no longer ships the original PDBs of the self-obfuscated CLI assemblies, and `build-installer.ps1 -SkipPublish` no longer deletes the publish folders it reuses
- `publish.yml` only runs for a release, `main` or a tag, and the installer and report-generator tool versions in CI are pinned
- The desktop app refuses to start a run whose output would replace one of its inputs, and the run bar says why and how to get out (pick another folder, or clear the folder). Before, an output folder that was the folder of an input silently overwrote the original, and a closed set also deleted the backup it had made. It checks every mode (one file, closed set, merge, C# files) and the default `*.obfuscated.*` name, ignoring case, trailing separators and `..`, and follows symbolic links and junctions. The CLI is unchanged and still writes where `-o` says
- The CI UI-automation step ran no tests: `ilammy/msvc-dev-cmd` exports `Platform=x64`, so the project-level `dotnet test --no-build` looked in `bin/x64/Release` and found no DLL. The step now clears `Platform`
- The CI UI-automation tests could not reach the page on the runner: its steps run as an administrator, and an elevated WebView2 host ignores `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, so the debug port never opened. The step now sets the machine policy `AdditionalBrowserArguments` for `ObfyUI.exe`, and the harness reads the port the browser picks from `DevToolsActivePort`. A port that still does not open now fails with whether the app is running, its visible windows, and whether the WebView2 failure panel shows
- The desktop app shows and runs a loaded configuration as Custom when its level-controlled options are not exactly what its `level` preset sets, so a run uses the file's switches, as the CLI does. The file on disk is not rewritten
- The run bar hint names what a run writes: with an output folder it gives the input's own file name (not `*.obfuscated.*`), and two or more C# files without a folder are compiled together into `obfy-src` next to the first
- Native packing is greyed out on the Protection screen for NativeAOT, Unity IL2CPP, and Blazor WebAssembly, as the engine already turned it off for them
- The Minimal level is described as symbol renaming and debug information removal in the app, the wizard, and the docs
- The Results tree nests members under their type. Method keys include the signature, for example `Method:Ns.MyType.Foo(System.Void ())`. Zero counters are hidden, and search waits briefly before filtering
- Control flow obfuscation with opaque predicates could intermittently produce a method the runtime refuses to load (`InvalidProgramException`). A predicate needs up to three evaluation-stack slots above the depth where it is inserted, but the method's declared maximum was only floored at 8, so one random predicate form at a six-deep site overflowed it. The declared maximum now covers the original depth plus the predicate headroom. This also removes the intermittent failure of the Virtualization full-pipeline tests, because the injected interpreter has such a site
- Files can be dropped on the window. A dropped `.json` file loads that configuration
- A saved output folder that no longer exists is cleared, and the window says it was not found. A desktop `-o` path is used for that session and is not saved as the default. Editing the box saves the new path, including a folder that does not exist yet
- File rows have an include checkbox, a status color, and the output path after success. Extended selection and Delete remove the selected rows. The row menu has Show in folder and Remove
- Obfuscate explains why it is disabled. The status text is the status bar name
- NativeAOT, Unity IL2CPP, and Blazor WebAssembly disable the protections those profiles cannot use and keep the stored values. Signing has a key-file browse button, and a missing key or an empty watermark id stays marked invalid
- An app built with the .NET SDK and protected with the Aggressive preset (or with AntiDebug, AntiDump or MethodEncryption on its own) no longer crashes at startup with `TypeLoadException: Could not load type 'System.DllNotFoundException'` (or `System.Runtime.InteropServices.Marshal`) before any user code runs. The injected code named those two types through the `System.Runtime` reference, whose facade does not carry them; they now point at `System.Runtime.InteropServices`, checked on net8.0, net9.0 and net10.0. Inputs built against mscorlib, netstandard or System.Private.CoreLib are unchanged

## [2.0.0] - 2026-11-20

### Breaking
- `packing.enabled` now emits a native win-x64 host that replaces the obfuscated PE. Set `packing.rid` to `portable` for the previous managed `{name}.launcher.exe`.

### Added
- General IL virtualization of eligible instance and static methods (`--virtualize` / `virtualization.enabled`): objects, non-generic calls, fields, `newobj`, `ldstr`, i4/i8/r4/r8; skips EH, generic methods/types/calls, byref, custom structs/`Nullable<T>`, switch, constructors, `typeof` (`ldtoken`), interpolators that allocate `DefaultInterpolatedStringHandler`, and `using` / enumerator-struct foreach (array foreach can be encoded); per-build opcode permutation + XOR; gated off NativeAOT / IL2CPP / Blazor WASM; off in every preset; deterrent, not confidentiality

### Fixed
- NuGet package versions are declared once in `Directory.Packages.props`
- The Windows CI job runs the FlaUI smoke tests against the Release build
- Pull requests run the NativeAOT platform test, and the platform job fails when a platform test is skipped
- NuGet publish rebuilds the native host before packing, and CI fails when that host's sources do not match the committed stamp
- CI and publish pin third-party actions to commit SHAs, and Dependabot updates those actions
- Windows CI builds the MSIX and the Inno installer
- NuGet publish takes its version from the release tag and fails when that version is already on NuGet
- The native packing AES key is random for each pack, so the `.obfycache` sidecar is no longer that key
- Encrypted string blobs are stored in one field RVA, so the static constructor does not grow by one instruction per byte
- A batch of two or more assemblies is renamed as one closed set, so a type shared between them keeps one name
- Several C# files in one run are renamed together, so a type shared between them keeps one name
- A typed output directory and the symbol-map toggle are saved when they change, not only when the app exits
- The Visual Studio project builds without CS8604 or the CVST005 analyzer crash
- Scenario builds strip the inherited `Platform` variable only for `dotnet` commands that do not set it themselves
- Scenario builds, publishes, and runs wait longer by default, and `OBFY_SCENARIO_TIMEOUT_FACTOR` can stretch those timeouts
- Source string encryption skips excluded interpolated strings and attribute arguments, matching plain literals
- A watermarked assembly still runs, and the watermark attribute constructor stores its id
- Packed runtimeconfig uses the payload framework reference instead of the Obfy host version when the assembly has no target-framework attribute
- Typing in an exclusion box keeps the selected preset
- A covariant array store in the VM throws `ArrayTypeMismatchException`
- `newarr` with a negative length throws `OverflowException`
- Exceptions thrown by methods the VM calls keep their original stack trace
- `--report` on a multi-file run includes every successful assembly, not only the first
- Passing `--level` together with `--config` prints a warning that the config file wins
- `config wizard` on a redirected terminal prints a one-line error instead of a stack trace
- `config generate --level` with an unknown level prints a one-line error instead of a stack trace
- The VS Code problem matcher captures file, line, and column from `path(line,column): error|warning: message`
- Coverage collection includes `Obfy.PackedBootstrap`
- Project path matching is case-sensitive on Linux and case-insensitive on Windows
- Settings load keeps corrected values in memory when the corrected file cannot be saved
- A closed-set run stops before renaming when a member assembly fails to load, and writes nothing
- Constant encryption no longer throws on `int.MinValue` or `long.MinValue`
- Source string encryption leaves compile-time constants alone and parenthesizes interpolation holes instead of calling `ToString()`
- The VM keeps a boxed int in an `object` slot as a reference, and `conv.u4` from a float keeps the unsigned range
- Unreadable `.resources` files are skipped instead of failing string encryption; loaded `obfy.json` values the settings panel does not edit survive Obfuscate and Save
- Source renaming updates extension-method call sites, and opaque predicates no longer hide `out var` declarations
- An unresolved instance `call` is not virtualized; enum returns stay out of the VM
- Each closed-set run gets a fresh name generator
- The incremental cache key includes the signing key file and embedded dependency bytes
- AES decrypt rejects a payload shorter than the IV; string encryption keeps lone surrogates as raw UTF-16
- Method-body decryption treats a module handle of `-1` as unmapped
- Visual Studio argument quoting keeps a trailing backslash; the installer build stops when self-obfuscation fails; the native host restore log names the host-pack version
- Reference proxies for private and protected methods are nested in the declaring type, and external proxies are keyed by assembly so same-named methods stay distinct
- Public-API preservation keeps protected virtual property and event accessor names; closed-set rename distinguishes same-named nested types; WinForms `.resources` names, indexer `DefaultMemberAttribute`, and overload symbol-map keys stay consistent after a rename
- Debug-info removal strips assembly-level `DebuggableAttribute`; documentation stripping keeps non-doc `*.xml` resources; short attribute-name exclusions match
- Junk methods are valid IL; anti-dump neutralization failures stay inside a handler; anti-debug no longer exits when the tick gap exceeds one second; `TickCount64` opaque predicates branch on a 32-bit int
- Docs no longer claim `SuppressIldasmAttribute` blocks current disassemblers
- Linux CLI solution analysis resolves Visual Studio `.sln` backslash paths (`Lib\Lib.csproj`) so `obfy App.sln` finds projects on Ubuntu
- CI no longer installs a non-existent SDK `8.0.6`; `build.ps1` restores host pack `Microsoft.NETCore.App.Host.win-x64` 8.0.6 from NuGet and builds PackedBootstrap as AnyCPU so `Platform=x64` from the MSVC prompt does not hide the DLL
- CI rebuilds the native host on windows-latest but does not fail when the EXE differs from the checked-in dist (MSVC 14.51 vs the committer's toolchain); the rebuild is uploaded as an artifact
- Scenario `dotnet build` children no longer inherit `Platform=x64` from the MSVC prompt, so output stays `bin/Release`
- Closed-set / solution runs pack entry-point outputs (and copy packing sidecars) instead of silently leaving managed PEs
- Native packer publishes `.runtimeconfig.json` only after a successful PE replace, prefers the packed app TFM, and does not delete-then-move on non-Windows
- Platform MAUI scenario no longer times out on first-run `dotnet new maui` (180s; weekly job timeout 45 minutes)
- Store MSIX no longer sets `AppListEntry=none` on the CLI entry (Partner Center rejects that as a headless app)
- `obfy config generate` writes PascalCase string enums and omits null signing paths so the file matches `schemas/obfy.schema.json`
- VM `ToClr` boxes and stores enums from I4/I8 bits (`Enum.ToObject`) instead of a null `Ref`
- VM `cgt.un` / `clt.un` on refs implement CIL null checks (`o != null`, `o is T`) instead of comparing unset `Bits`
- VM encoder skips methods that contain a non-virtual `call` to a virtual instance method (`base.M()`), so those bodies stay original IL instead of stack-overflowing at runtime
- VM null instance/array access throws `NullReferenceException` instead of `TargetException` / `InvalidOperationException`
- VM `ldstr` interns literals so identity compares of equal strings succeed
- Virtualization fails the run if a selected method cannot be re-encoded (CallVm ids stay aligned with stubs)
- Reference proxy again trampolines string/constant decryptor calls from user IL; only `Obfy.Runtime.Vm` stays direct

### Changed
- Native packing / native EXE is documented as Yes (win-x64 FDD CLR-host stub; `portable` keeps the managed launcher; not Pre-JIT; not self-contained; not ARM64)
- `--virtualize` help: `Enable IL virtualization of eligible methods (no EH/generics/byref/custom structs/ctors; gated off NativeAOT / IL2CPP / Blazor WASM)`
- Desktop toggle label `Virtualize methods` (was `Virtualize simple methods`)
- Competitive analysis: code virtualization **Partial** with the eligibility limits footnote (Babel-class managed VM; does not cover Reactor or Babel Ultimate EH/generics/byref); closed-set / Store / Linux CLI job; MSBuild and Azure DevOps marked Partial; BitMono no longer listed as having a `dotnet tool` Obfy lacks; GitHub stats as of 15 Sep 2026
- CI runs `Obfy.Console.Tests` on Ubuntu so the cross-platform CLI claim is exercised every PR
- Coverage include list adds `Obfy.VmRuntime`; incremental-cache tests no longer pin assembly version `1.3.0.0`
- GitHub Release NuGet publish uses Trusted Publishing (OIDC) instead of a long-lived API key
- Microsoft Store identity is in `package/store-identity.json`; `.\build\build-msix.ps1 -Store` packs an unsigned Store MSIX; privacy policy is `PRIVACY.md`
- README shows the Obfy logo and the official Microsoft Store badge instead of a small shields.io pill
- Documentation: install paths (Store, GitHub Setup.exe, VS VSIX and Rider zip from source), CLI `-o` / closed-set / `-c` vs `-l`, virtualization ISA, privacy settings location, test counts, roadmap 1.3.0 status

## [1.3.0] - 2026-09-13

### Added
- In-app Help dialog (toolbar Help and F1) with a Getting started catalog
- CLI ships as a .NET tool again (`dotnet tool install --global Obfy`); CI packs the nupkg and GitHub Releases push it to nuget.org
- Drop or pass a Visual Studio solution/project to obfuscate included assemblies as one closed set (in-solution public APIs can be renamed together; test projects and missing `bin` outputs are skipped)
- `obfy App.dll Lib.dll` (two or more existing assemblies, no `.sln`/`.csproj`) uses the same closed-set path as a solution drop; `--merge` still merges. Without `-o`, output is `{first-assembly-dir}/obfy-out/` instead of sibling `*.obfuscated.dll` files

### Changed
- Competitive analysis rewritten against 2026 vendor pages and GitHub stats (ArmDot/DNGuard/Agile.NET added; LoGiC.NET archived; Obfy positioned as conventional protection, not a general VM)
- NuGet packages: Autofac 9.3.2, Microsoft.Extensions.* 10.0.12, NLog 6.2.0, System.CommandLine 2.0.12, Roslyn 5.9.0, ILSpy decompiler 11.0.0, WPF-UI 4.3.0, Spectre.Console 0.57.2, test SDK 18.10.0
- VS 2022 extension SDK/BuildTools stay on 17.14 (not VSSDK 18)

### Fixed
- Closed-set rename rewrites generic MemberRefs from CIL and MethodSpec (not dnlib `GetMemberRefs()` copies), so `Box<int>.Id(...)` still runs after in-set public names change
- CLI closed-set of loose DLLs fails (exit 1) when a listed assembly is missing or fails to load, including `--dry-run`
- Closed-set UI no longer overwrites input assemblies when the output directory is empty (writes `{inputDir}/obfy-out` instead)
- Closed-set rename matches TypeRefs by assembly name and TFM so multi-TFM copies of the same assembly do not steal each other's names
- Closed-set commit copies then replaces dest files; restore failures include `*.obfyprev` backup paths in the error
- CLI session: missing extras and load failures exit 1; extras print `After load` for library-mode until AssemblyRef is known
- Solution/project XML parse failures skip that project instead of aborting the session
- VS/Rider Aggressive `obfy.json` now writes `protection.methodEncryption: true` and `proxyExternalCalls: false` (Core/CLI already did; plugins omitted them because config files skip `ApplyLevel`). Rider's settings dialog round-trips those flags on save.
- Symbol renaming no longer special-cases the `Obfy.Core.Models` namespace in target assemblies (types, members, and the namespace string)
- VS and Rider save/post-build toggle merge into existing `obfy.json` instead of rewriting a subset (Core-only keys such as virtualization, packing, and exclusions are kept)
- In-place IDE overwrite copies packing sidecars and `.obfycache`, fails if temp output is missing, stages then replaces the live assembly, and deletes the temp dir in `finally`
- Incremental cache write I/O errors are a warning, not a failed run; `Obfy.Core` is versioned `1.3.0` so upgrade keys actually change
- Log copy clipboard failures surface in the UI; CLI unobserved/unhandled exceptions print the full exception and flush NLog
- Incremental cache keys include the Obfy assembly version so an upgrade is not a false cache hit; a locked or corrupt `.obfycache` is a miss instead of a failed run
- Pipeline cancellation rethrows `OperationCanceledException` so CLI/UI cancel is not reported as an obfuscator error
- VS and Rider invoke `obfy -c obfy.json` instead of reconstructing a subset of flags; in-place overwrite writes to a temp file then copies back
- VS Tools → Options is read via `GetDialogPage`; command visibility no longer blocks the UI thread with `JoinableTaskFactory.Run`
- Managed launcher loads the embedded assembly from a stream (no `{name}.payload.dll` on disk); anti-tamper skips ALC loads instead of hashing the host, and packing+anti-tamper emits an explicit warning
- Source directory save preserves relative subfolders; compilation uses trusted platform assemblies
- `ApplyLevel` resets control-flow mode, virtualization, packing, and incremental so Aggressive leftovers do not leak into Standard/Minimal
- Enter in text boxes no longer starts obfuscation (`IsDefault` removed; Ctrl+Enter remains)
- Clipboard copy failures surface in the UI instead of being swallowed
- Assembly write calls `SimplifyBranches` / `OptimizeBranches` so control-flow on async state machines (Blazor WASM `MoveNext`) no longer fails with “short branch too far”
- Unity recipe and wizard exclude `UnityEngine` / `Unity` as well as `UnityEngine.*` / `Unity.*` (`*` does not match the namespace itself)
- CI requests `pull-requests: write` for the sticky coverage comment, skips the comment on fork PRs, and sets `continue-on-error` on that step so a 403 (fork/read-only token) cannot skip the coverage summary or 80% warning
- CLI config files accept camelCase enum values (`"level": "aggressive"`)
- `preserveXaml` View/ViewModel suffix match is case-insensitive (`Overview` / `Preview`)
- Virtualization skips unsigned compares (`cgt.un`, `b*.un`) instead of executing them as signed
- Dark-theme UI text uses theme foreground brushes so file names, settings, and logs stay readable
- Disabled toolbar buttons (Obfuscate with no files) keep readable label and border contrast
- CI `dotnet test` no longer fails 18 FlaUI tests that looked for Debug `ObfyUI.exe` after a Release build; live-window tests are `Category=UI` (opt-in) and the locator prefers the current configuration
- Packing writes the incremental cache only after a successful launcher emit; cache hits require the launcher files
- Preview failures no longer fail a successful obfuscation run
- Packed host awaits async Main and resolves sibling assemblies from the load context (payload stays in-memory)
- Anti-dump MiniDumpWriteDump patch is skipped unless the process is X86/X64 (ARM64 is no longer written with `0xC3`)
- Requested watermark/decoy skips are reported as warnings instead of silent success
- `--watermark-id` with only whitespace is an error instead of a silent no-op
- External reference proxy skips `constrained.` prefixes (foreach/`using` on structs) so the output stays verifiable
- Resource encryption hard-skips `Obfy.Embedded.*` even if `excludePatterns` is overwritten
- UI load/save/run preserves `dependencyEmbedding` instead of dropping it
- `--proxy-external` help text matches that it enables `--reference-proxy`
- Rider JVM CI compiles against Rider 2024.3 (`com.jetbrains.rd.protocol.SolutionExtListener`)
- VS CLI stats parser reads Spectre table rows as well as colon summary lines
- Anti-tamper `Verify` FailFasts on IO/crypto exceptions, not only hash mismatch
- `Virtualization.MaxMethods` out of range fails instead of clamping

### Added
- `RuntimeInjection` / `RuntimeHelperOptions`: injected `Obfy.Runtime` helpers register flatten/rename/encrypt-IL policy. Unregistered `Obfy.Runtime` types (including nested types) do not flatten or encrypt IL; CFG no longer keys off the `<Vm>` type name
- CLI `Program.Main` tests for dry-run, missing file, malformed JSON, and unknown `--level`
- Anti-tamper child-process test that a tampered PE FailFasts (payload does not run)
- Pipeline fail-closed tests (Failed result, throw, cancel, target-type filter)
- Source string encryption compile-and-run; merge fail-closed; merge-then-obfuscate; signing wrong PFX / truncated SNK
- Logging.Core factory/module tests; VS `ObfyCliLocator`; UI `ApplyStartup` command-line apply
- Coverlet include filter (`coverage.runsettings`); Rider JVM tests on CI; MAUI workload install on the weekly platform job
- CLI flags `--virtualize` and `--incremental`
- Desktop UI toggles for virtualization and incremental cache
- `IPePostProcessor` for PE post-write steps (method IL XOR, integrity hash)
- Engine-gap tests: virtualization skip/encode, `IncrementalCache.TryHit` invalidation, two-file source rename compile-and-run
- Tooling tests: VS settings JSON / CLI args / output-assembly locator (no hive), Rider settings + `AssemblyLocator` JVM tests, FlaUI command-line DLL obfuscate path, Settings.Core validation
- Platform scenario tests: published Blazor WASM `_framework` DLL, NativeAOT obfuscate-then-publish, MAUI Windows (skip without workload)
- SDK scenario tests (`Obfy.ScenarioTests`): shipped examples, WPF app/solution, console+lib, WinForms, satellites, Unity stub, and MSBuild AfterBuild compile → obfuscate → run
- Desktop UI opens input files and `-o`/`--output` from the command line
- Incremental obfuscation cache (`incremental.enabled`)
- Selective IL virtualization for simple static int methods (`virtualization.enabled`)
- Virtualization encodes locals, comparisons, and branches (`ldloc`/`stloc`, `ceq`/`cgt`/`clt`, `if`/`else`), and allows zero-argument static int methods (still ≤8 params / ≤16 int locals; unsigned compares are skipped)
- Framework-dependent managed launcher (`packing.enabled`): writes `{name}.launcher.exe` + `.runtimeconfig.json`; run with `dotnet`. Requires an entry point. Not a native/unmanaged packer.
- Results Preview tab: ILSpy-engine C# of the last successful output assembly (truncated; not the launcher)
- MSIX packaging for Microsoft Store / sideload (`build/build-msix.ps1`)
- Desktop UI snackbars for save/load/complete/fail, keyboard shortcuts, and an Open output folder action
- Settings panel controls for anti-decompiler, assembly merge, exclusions, and tamper-check sites
- File-list status, size, source vs assembly icons, and error text after a run
- Anti-dump PE-header wipe at module load (Aggressive preset)
- Reference proxy for in-module method calls (Aggressive preset)
- Native `IsDebuggerPresent` check in addition to managed debugger APIs
- `calli` reference proxies (ILSpy cannot inline a `call`+`ret` trampoline)
- XOR-encoded string decrypt indices so call sites are not `Decrypt(0)`, `Decrypt(1)`, …
- Method IL encryption: XOR method bodies in the PE and decrypt them at module load (Aggressive)
- Symbol renaming for events, accessors, and namespaces
- CFG control-flow flattening for methods with branches
- Opaque predicates based on `Environment.TickCount` instead of foldable constants
- CLI flags `--anti-dump`, `--reference-proxy`, `--encrypt-constants`, `--no-control-flow`
- JSON Schema for `obfy.json` (`schemas/obfy.schema.json`); `obfy config generate` writes `$schema`
- Directory.Build.props, `.editorconfig`, and `version.json`
- Decompiler-resistance fixtures (ILSpy decryptor output, anti-debug NOP survival, method-encryption skip counts in reports)
- Default JSON/XML attribute exclusions, `ComVisible(true)` skip, and `symbolRenaming.preserveXaml` for XAML bindings
- `System.Reflection.ObfuscationAttribute` and optional `inclusions` allow-list
- `runtimeProfile` (NativeAot / UnityIl2Cpp / BlazorWasm) disables method encryption, anti-dump, and dependency embedding with a warning
- Strong-name re-signing after obfuscation (`signing.keyFile`; PFX password from `signing.passwordEnvironmentVariable`, required for `.pfx`/`.p12`)
- Optional `proxyExternalCalls` / `--proxy-external` to hide selected out-of-module call targets (`--proxy-external` enables `--reference-proxy`)
- Dependency embedding (`dependencyEmbedding.enabled`) loads sibling referenced DLLs from resources via AssemblyResolve
- Unity / Blazor / MAUI recipes and wizard presets; `runtimeProfile: BlazorWasm`
- VS Code extension stub (`Src/Obfy.VSCode`) with `obfy.json` schema and problem matcher (no custom task type)
- Watermark (`watermark.enabled` + `watermark.id`; empty/whitespace `id` is rejected). Type name `WatermarkAttribute` is pinned against renaming; the id is a plaintext CA constructor argument and `Id` field
- Decoy `ConfusedByAttribute` / `DotfuscatorAttribute` (`protection.antiDecompiler.addDecoyAttributes`, default true when anti-decompiler is on). Names are pinned against renaming. Name-based detector bait; does not block de4dot
- Anti-dump also overwrites the first byte of in-process `dbghelp!MiniDumpWriteDump` with x86/x64 `ret` (`0xC3`) after an X86/X64 architecture check (Windows; ARM64 is skipped; `VirtualProtect` failure skips the write). External dumpers are unaffected.
- NativeAOT / Unity IL2CPP / Blazor WASM anti-debug keeps managed checks only (no kernel32 P/Invoke) and emits a report warning
- CLI `--watermark-id` and Settings panel watermark / decoy-attribute controls

### Fixed
- Assembly merge uses `ILRepack.Lib` 2.0.48 so `--merge` works on a net10 host (was `ILRepack.NETStandard` 2.0.4 `NotSupportedException`)
- `ObfySettings.Validate` enforces `Virtualization.MaxMethods` range
- Anti-tamper integrity failure uses `Environment.FailFast` so the process cannot continue after a failed check
- Settings panel FlaUI toggle no longer passes silently when the Toggle pattern is missing
- MSBuild AfterBuild scenario test name no longer claims the app is run
- Assembly write calls `SimplifyBranches` / `OptimizeBranches` so control-flow on async state machines (Blazor WASM `MoveNext`) no longer fails with “short branch too far”
- Unity recipe and wizard exclude `UnityEngine` / `Unity` as well as `UnityEngine.*` / `Unity.*` (`*` does not match the namespace itself)
- CI requests `pull-requests: write` for the sticky coverage comment, skips the comment on fork PRs, and sets `continue-on-error` on that step so a 403 (fork/read-only token) cannot skip the coverage summary or 80% warning
- CLI config files accept camelCase enum values (`"level": "aggressive"`)
- `preserveXaml` View/ViewModel suffix match is case-insensitive (`Overview` / `Preview`)
- Virtualization skips unsigned compares (`cgt.un`, `b*.un`) instead of executing them as signed
- Dark-theme UI text uses theme foreground brushes so file names, settings, and logs stay readable
- Disabled toolbar buttons (Obfuscate with no files) keep readable label and border contrast
- CI `dotnet test` no longer fails 18 FlaUI tests that looked for Debug `ObfyUI.exe` after a Release build; live-window tests are `Category=UI` (opt-in) and the locator prefers the current configuration
- Packing writes the incremental cache only after a successful launcher emit; cache hits require the launcher files
- Preview failures no longer fail a successful obfuscation run
- Packed host awaits async Main and resolves sibling assemblies from the load context (payload stays in-memory)
- Anti-dump MiniDumpWriteDump patch is skipped unless the process is X86/X64 (ARM64 is no longer written with `0xC3`)
- Requested watermark/decoy skips are reported as warnings instead of silent success
- `--watermark-id` with only whitespace is an error instead of a silent no-op
- External reference proxy skips `constrained.` prefixes (foreach/`using` on structs) so the output stays verifiable
- Resource encryption hard-skips `Obfy.Embedded.*` even if `excludePatterns` is overwritten
- UI load/save/run preserves `dependencyEmbedding` instead of dropping it
- `--proxy-external` help text matches that it enables `--reference-proxy`
- Rider JVM CI compiles against Rider 2024.3 (`com.jetbrains.rd.protocol.SolutionExtListener`)
- VS `obfy.json` save patches known keys instead of rewriting the document
- VS CLI stats parser reads Spectre table rows as well as colon summary lines
- Anti-tamper `Verify` FailFasts on IO/crypto exceptions, not only hash mismatch

### Changed
- README includes screenshots of the desktop application
- Documentation matches the Unreleased pipeline: README, CLI, configuration, techniques, roadmap, competitive analysis, test counts, and examples (net10.0). Encryption is documented as obfuscation, not confidentiality.
- Strong-name signing refreshes the signature blob in place after method-IL XOR; anti-tamper hashing skips the signature so Aggressive + `signing.keyFile` keeps both protections
- Method IL encryption records generic skips in the report and uses a distinct XOR key per method; warns when many methods are skipped because they are generic
- Method IL encryption always warns that it is Windows-only / not NativeAOT; decrypt failure leaves ciphertext
- Anti-debug scatters `Check` into user methods, adds `CheckRemoteDebuggerPresent` and a TickCount timing probe, and cycles failure through `Exit` / `FailFast` / `throw` inside `Check`
- Linear control-flow flattening uses random `beq` dispatcher states instead of sequential `switch` indices
- Opaque predicates draw from `ProcessorCount`, `CurrentManagedThreadId`, `TickCount64` (modern .NET only), and `GC.MaxGeneration` as well as `TickCount`
- Runtime helpers (decryptors, anti-debug `Check`, anti-tamper `Verify`, anti-dump `Wipe`, method-body decrypt) are control-flow obfuscated when control flow is on; `.cctor` and P/Invoke are left alone
- String decryption uses three entry points; call sites round-robin so there is no single `Decrypt(int)` for every string
- Reference proxies now hide user calls to assembly-visible runtime helper entry points
- Product roadmap retargeted after the protection investigation: harden existing techniques and compatibility before a custom VM
- WPF-UI 4.2.1 with `ContentDialogHost`, system theme, and CardExpander settings groups
- UI level presets now match Core `ApplyLevel()`; dropdowns use human-readable descriptions
- Add Files accepts `.cs` as well as assemblies; empty output path is documented as `*.obfuscated.*`
- Multi-file runs export a combined report; merge and symbol-map options actually run
- String and constant encryption now cover try/catch, compiler-generated methods, and compiler-generated types (async/iterator/lambda display classes)
- Control flow applies opaque predicates to methods with exception handlers (`using`/`await`/`try`) instead of skipping them
- Opaque predicates use several always-true forms plus a junk dead branch, not only `n*(n+1)%2`
- Anti-dump wipes MZ, checksum, import, debug, and IAT directory slots (not only PE checksum)
- Anti-tamper falls back to `Environment.ProcessPath` when `Assembly.Location` is empty; `Verify` is assembly-visible
- Anti-tamper magic marker is no longer the ASCII string `OBFY_AT_MAGIC!!`
- Resource-string encryption prefixes ciphertext so `ResourceManager.GetString` leaves short/plaintext values readable
- IDE `obfy.json` uses the same nested schema as the CLI
- VS Obfuscate writes into the project output directory; Rider post-build actually runs after a successful build
- WPF app bootstraps with `IHost`, follows the system theme, and uses Fluent snackbar/content dialogs
- Runtime helpers are renamed and use encoded keys; ciphertext is stored as byte arrays
- Hash naming is salted per run so original names are not a dictionary oracle
- Resource encryption rewrites `GetManifestResourceStream(Type, string)` and `Module` overloads
- Anti-debug also checks `Debugger.IsLogging()`
- Junk types no longer live in the `Obfy.Internal` namespace

### Fixed
- Opaque predicates no longer emit `Environment.TickCount64` on .NET Framework / netstandard 2.0
- Method IL encryption no longer documents a plaintext fallback; decrypt failure leaves ciphertext
- UI settings no longer disagree with what Aggressive/Minimal/Standard actually apply
- Failed files no longer look identical to pending ones; mid-run exceptions reset processing status
- Export report/map I/O failures are shown instead of failing silently
- Dead About window with a hardcoded 1.2.0 version removed
- `exclusions.Methods` is honored by assembly symbol renaming
- `encryptConstantStrings` / `encryptResourceStrings` settings now take effect
- Source interpolated string parts are encrypted
- Obfuscation no longer mutates the caller's settings object when applying a level preset
- Aggressive and AES obfuscation now produce loadable, runnable assemblies (constant decryptors, empty-stack control-flow splits, crypto types referenced from their real assemblies)
- Source switch flattening no longer emits CS0161, wraps declarations, or splits `out var` / local functions across cases
- Source symbol renaming is semantic (ISymbol), including attributes and record positional properties
- String/constant encryption and control-flow record skipped try/catch methods instead of silently leaving them unprotected
- Resource encryption decrypts only encrypted names, rewrites only `Assembly.GetManifestResourceStream(string)`, and warns on other load paths
- Control-flow fails the run instead of returning success after a half-rewritten method body, and does not split IL prefixes
- Anti-debug warns when no call site could be instrumented; anti-tamper fail-closes if the hash blob is missing
- Skips and warnings are shown in CLI/UI output and exported reports, not only in `--report`
- UI overall status reports file failures
- Assembly string encryption now decrypts at runtime (AES-256 and XOR) instead of returning ciphertext
- Anti-tamper hash excludes the stored digest so Aggressive builds no longer exit on startup
- Resource encryption keeps embedded resources and unwraps `GetManifestResourceStream`
- Custom CLI flags, UI toggles, and IDE settings are no longer overwritten by level presets
- CLI exits with code 1 and does not print success when obfuscation fails
- Added `--anti-tamper`, `--anti-decompiler`, `--no-string-encryption`, and `--no-symbol-renaming` so VS/Rider Custom mode matches the CLI
- Symbol renaming no longer breaks virtuals or implicit interface implementations
- Source obfuscation no longer rewrites attribute arguments or leaves constructors unrenamed
- Source switch flattening skips methods with locals/`break`/`continue`
- Missing config files, invalid `--level`, and `--merge` with one file now error instead of silently continuing
- CI runs on Windows so WPF projects can build
- Resource-string decrypt hook no longer `FromBase64String`s unencrypted short strings (would throw at runtime)
- UI last-output-directory / symbol-map preferences are saved and restored
- VS/Rider Custom mode can enable anti-dump, reference proxy, and constant encryption
- VS Generate Symbol Map option now passes `--map`

## [1.2.0] - 2026-01-21

### Added
- JetBrains Rider plugin for IDE integration (DX-04)
  - Right-click "Obfuscate" command on projects
  - Tool window with obfuscation output
  - Settings panel with level presets
- UI automation tests with FlaUI (QT-04)
  - Test project: Tests/Obfy.UI.AutomationTests
  - FlaUI.UIA3 framework for WPF automation
  - AutomationIds added to all key XAML controls
  - Tests for application launch, settings panel, about dialog
  - Page object model with MainWindowElements helper
  - Local-only execution (requires interactive Windows desktop)
- Code coverage CI integration (QT-03)
  - Coverage collection with Coverlet during CI builds
  - HTML coverage reports via ReportGenerator
  - Coverage summary in GitHub Actions job summary
  - PR comments with coverage details
  - Coverage report artifacts downloadable from CI
  - CI status badge in README
- Comprehensive test coverage for CLI and UI (QT-01, QT-02)
  - 92 CLI tests: argument parsing, help output, error handling, integration
  - 59 UI ViewModel tests: FilesViewModel, SettingsViewModel commands
  - New Obfy.UI.Tests project with xUnit, Moq, Shouldly
  - Testing documentation guide at docs/Testing.md
- Self-obfuscation in build process to protect Obfy's own assemblies
  - Obfuscates CLI and UI assemblies before packaging into installer
  - Uses copy-then-obfuscate approach to solve bootstrap problem
  - Applies string encryption, symbol renaming, and metadata removal
  - New `-SkipObfuscation` flag for build-installer.ps1
- Configuration Wizard for interactive config generation (DX-05)
  - `obfy config wizard` command with Quick and Advanced modes
  - Quick mode: Select application type and protection level
  - Advanced mode: Step through all settings interactively
  - Use case presets for Desktop, Library, Web, Console, Unity
  - Spectre.Console rich prompts and summary table
- Visual Studio 2022 Extension for IDE integration (DX-02)
  - Right-click "Obfuscate" command on projects
  - Toggle post-build obfuscation per-project
  - Settings dialog with level presets
  - Output window integration
- Assembly merging to combine multiple DLLs into one (UF-01)
  - Merge multiple assemblies before obfuscation for single-file output
  - Internalize merged types (makes public types internal for better obfuscation)
  - Exclude patterns for framework assemblies (System.*, Microsoft.*)
  - `--merge` and `--internalize` CLI options
  - Configurable via `assemblyMerge` settings in obfy.json
- Anti-decompiler protection to make reverse engineering harder (PF-04)
  - Injects junk types and methods to clutter decompiler output
  - Adds SuppressIldasm attribute (current disassemblers ignore it)
  - Configurable junk type count and methods per type
  - Uses confusing Unicode characters for junk names

### Fixed
- Exclude Obfy.Core.Models namespace from self-obfuscation
- Rider plugin CLI path quoting and blank tool window

## [1.1.0] - 2026-01-01

### Added
- WPF desktop application with Fluent Design (DX-01)
  - Three-panel layout (Settings, Files, Output)
  - Drag-and-drop file support
  - Level presets with expandable advanced settings
  - Real-time progress and color-coded output
  - Results visualization with symbol map export
- Anti-tamper detection to verify assembly integrity at runtime (PF-03)
  - Computes SHA-256 hash of method bodies during obfuscation
  - Injects runtime verification at entry point and/or module initializer
  - Graceful fallback for single-file published apps
  - Configurable via `protection.antiTamper` settings
- Resource encryption obfuscator to encrypt embedded resources (PF-01)
- Constant encryption obfuscator to encrypt numeric literals (PF-02)
  - Supports int, long, float, double constants
  - Configurable thresholds to skip small/common values
  - XOR encryption for fast runtime decryption
- Obfuscation report generation (UF-04)
  - Generate detailed HTML reports with visual styling
  - Generate JSON reports for machine processing
  - `--report <file>` CLI option (format based on extension)
  - "Export Report" button in UI results panel
  - Reports include: statistics, file size comparison, processing times, warnings
  - Warnings for unused settings, skipped items, and public API changes
- `--encrypt-resources` CLI option
- Pattern-based include/exclude for resource encryption
- Support for AES-256 and XOR algorithms for resources
- Inno Setup installer with PATH registration

### Changed
- Remove dotnet tool support in favor of installer-based distribution

## [1.0.6] - 2025-12-31

### Fixed
- Preserve branch targets when encrypting strings

## [1.0.5] - 2025-12-31

### Fixed
- Skip compiler-generated code in string encryption

## [1.0.4] - 2025-12-31

### Changed
- Add comprehensive documentation for public library
- Add GitHub infrastructure (issue templates, PR template)

## [1.0.2] - 2025-12-31

### Fixed
- Prevent invalid IL in string encryption

## [1.0.1] - 2025-12-31

### Added
- Single-file executable troubleshooting guide

## [1.0.0] - 2025-12-31

### Added
- Configure Obfy as a .NET global tool
- Comprehensive test coverage for obfuscators and services
- Comprehensive documentation

## [0.1.0] - 2025-12-31

### Added
- Initial release of Obfy obfuscation tool
- String encryption with AES-256 and XOR algorithms
- Symbol renaming with multiple naming modes (unreadable, sequential, hash, random)
- Control flow obfuscation with switch flattening and opaque predicates
- Anti-debugging protection injection
- Metadata removal (debug info, attributes, documentation)
- CLI with System.CommandLine
- JSON configuration file support
- Obfuscation level presets (minimal, standard, aggressive, custom)
- Symbol map export for debugging
- Batch processing of multiple files
- Support for .NET assemblies (DLL/EXE)
- Roslyn-based source code processing infrastructure
