# Solution session and closed-set protection

**Date:** 2026-09-13  
**Status:** Draft for review  
**Surfaces in v1:** CLI + WPF desktop app  
**Not in v1:** Visual Studio, Rider, MSBuild target injection, auto-build

## Goal

Obfy accepts a Visual Studio solution or project the way Dotfuscator accepts an application: drop or pass the `.sln` / `.csproj`, analyze the graph, protect the **ship set** that already exists on disk, and treat those assemblies as **one closed application** so in-solution public APIs can be renamed together.

This is “analyze then obfuscate now,” not “wire every future Release build.” Missing outputs and test projects are skipped with a report. Nothing is built by Obfy.

## Success criteria

- `obfy MyApp.sln -o out/` and dropping the same `.sln` in the desktop app produce the same included set and skip reasons.
- An app + in-solution library: the library’s public types used only by that app **are renamed**, and the app still runs.
- A libraries-only solution: public API **is preserved**.
- Test projects and missing `bin` outputs are skipped, never fail the process by themselves.
- A pipeline failure commits **no** output for that session.
- Existing `obfy App.dll` (single assembly) behavior is unchanged.

## Architecture

```
.sln / .slnx / .csproj
        │
        ▼
 SolutionAnalyzer
        │
        ▼
 ProtectionSession
        │
        ▼
 ClosedSetProcessor
        ├── SymbolRenaming across all included modules (once)
        └── remaining obfuscators per module
        │
        ▼
 output directory + report
```

New types live in `Obfy.Core` so CLI and UI share one implementation. `ObfuscationService` keeps the single-file path; it delegates to `ClosedSetProcessor` when the input is a session (solution/project) or when two or more assemblies are processed together.

## Components

### `SolutionAnalyzer`

Pure function: path → `ProtectionSession`. No MSBuild host, no restore, no `dotnet build`.

**Accepted inputs**

| Path | Behavior |
|------|----------|
| `.sln` | Parse `Project(...)` entries; keep `.csproj`, `.vbproj`, `.fsproj`. Ignore solution folders and other project types. |
| `.slnx` | Same, from the XML project list. |
| `.csproj` / `.vbproj` / `.fsproj` | Single-project session. |

Relative paths resolve from the solution directory. A listed project file that is missing → `SkipMissingProject`.

**Project XML (no evaluation of `Directory.Build.props`)**

Read:

- `OutputType` (SDK class libraries default to `Library`)
- `TargetFramework` / `TargetFrameworks`
- `AssemblyName` (fallback: project file name)
- `UseWPF`, `UseWinForms`, `UseMaui`
- `PublishAot` / `IsAotCompatible`
- SDK: `Microsoft.NET.Sdk.Web`, `Microsoft.NET.Sdk.BlazorWebAssembly`, `Microsoft.NET.Sdk.Razor`
- `IsTestProject`; test SDKs (`MSTest.Sdk`, `Microsoft.NET.Test.Sdk`); `xunit` / `nunit` / `mstest` package references
- `ProjectReference` items (closed-set graph for UI/CLI plan)
- Unity hint: `Reference` / `PackageReference` / `HintPath` containing `UnityEngine`

Do not expand wildcards or `$(Property)` except by searching conventional `bin` layouts on disk.

**Skip rules (first match wins)**

1. Test-like → `SkipTest`  
   `IsTestProject`, test SDK/package, or project **file name** (without extension) that equals `Test`, ends with `Tests` / `.Tests` / `.Test` / `.Testing`, or contains `.Tests.`. Do not use a bare `*Test` suffix (`Contest.csproj` is not a test).
2. No output on disk → `SkipMissing`.
3. Otherwise → `Include`.

**Output discovery**

Search, per TFM (and the non-TFM `bin/Release` layout for old-style projects):

`{projectDir}/bin/{Release,Debug}/{tfm?}/{AssemblyName}.{dll|exe}`

Prefer **Release** when both exist. Multi-TFM: include every TFM that has a file.

**Hints on included projects**

| Detection | Overlay on the user’s `ObfySettings` clone |
|-----------|--------------------------------------------|
| `UseWPF` / `UseWinForms` / `UseMaui` | `symbolRenaming.preserveXaml = true` |
| `PublishAot` / `IsAotCompatible` | `runtimeProfile = NativeAot` |
| Blazor WASM SDK or `net*-browser` TFM | `runtimeProfile = BlazorWasm` |
| UnityEngine reference | `runtimeProfile = UnityIl2Cpp` + existing Unity namespace excludes |
| Web SDK (non-WASM) | existing ASP.NET MVC attribute excludes |
| True library (see closed-set rule) | `symbolRenaming.preservePublicApi = true` |
| Library referenced by an included exe/winexe | `preservePublicApi = false` |

User `--level` / UI level still selects techniques. Hints never enable Aggressive extras. If `-c` JSON already sets `runtimeProfile` to a non-`Default` value, do not overwrite it.

**Closed-set public API rule**

- If an included **exe/winexe** references an included **library** (`ProjectReference` in the plan; `AssemblyRef` after load), that library is **not** library-mode: public names are renamed and sibling references are updated.
- If the session is **only libraries**, preserve public API on all of them.
- A skipped or missing library is **outside** the set: do not rename references to it.

After load, `AssemblyRef` is authoritative if it disagrees with `ProjectReference` (the built output is what we protect).

### `ProtectionSession`

Immutable plan:

- Solution/project path
- Entries: project path, name, output path or skip reason, hints
- Included modules (those with output)
- Warnings

Always enumerable for CLI stdout and the UI Files list.

### `ClosedSetProcessor`

Used when:

- The user passed/dropped a solution or project, **or**
- The UI/CLI is protecting **two or more** assemblies in one run.

A single assembly (plain `obfy App.dll`) stays on today’s `ObfuscationService` path.

**Load.** Open every included module with one dnlib `ModuleContext` and a resolver that maps in-set assembly names to those modules. Framework and out-of-set references resolve from disk as today. A module that fails to load becomes `SkipLoadFailed`; recompute the closed-set graph so a library whose only consumer dropped is not aggressively renamed.

**Rename once.** One `NameGenerator` across the session. Per-module `preservePublicApi` from hints. Existing skips still apply: `[Obfuscation]`, exclusion lists, `preserveXaml` heuristics, entry point, external overrides.

Then walk every in-set module for `TypeRef` / `MemberRef` / `MethodSpec` / `TypeSpec` that resolve to a renamed def and update those rows.

**Other techniques per module.** After shared rename, each module runs the rest of the pipeline with symbol renaming already applied (do not run `SymbolRenamingObfuscator` a second time). Each module gets a settings clone with its hints (`runtimeProfile`, `preserveXaml`, Unity excludes). `RuntimeProfileGating` stays as it is.

**Write is all-or-nothing.** Process in memory or a temp directory. If any included module’s remaining pipeline fails, commit **no** files. Cancel discards temp output.

**Output layout.** Default: `-o out/` → `out/App.dll`, `out/Lib.dll`. If two TFMs share an assembly name, keep the TFM segment (`out/net8.0/Lib.dll`, `out/net8.0-windows/Lib.dll`).

**`--merge`.** Existing merge runs first on the included set; then the single-module pipeline. Closed-set rename is not used after a successful merge.

## CLI

Inputs may be `.sln`, `.slnx`, `.csproj`, `.vbproj`, `.fsproj`, plus existing `.dll` / `.exe` / `.cs`.

- `obfy MyApp.sln -o out/` analyzes, logs the plan, runs the closed-set pipeline.
- Extra assemblies: `obfy App.sln Extra.dll -o out/` add Extra to the same closed set (library-mode via `AssemblyRef` after load).
- **Two or more solution files in one invocation:** error (ambiguous closed set).
- Several project files: allowed; treated as one synthetic session.
- `--dry-run`: print included paths, skip reasons, library-mode, `runtimeProfile` / `preserveXaml`; exit 0; no write.
- `--preserve-public`: force library-mode on all included libraries (escape hatch).
- `-c obfy.json`: base settings; analyzer hints overlay as specified above.

**Exit codes**

| Code | When |
|------|------|
| 0 | Every included module written, or dry-run completed |
| 1 | Pipeline failed; no output committed |
| 2 | Zero included assemblies (all skipped) |

## Desktop UI

Drop and Add Files accept the same extensions as CLI. Empty-state and drop overlay text: assemblies **or** a solution.

A solution/project drop **expands** into the Files list:

- Included outputs: normal pending rows.
- Skipped projects: rows with status `Skipped` and a visible reason. Not processed.

Drop does **not** start obfuscation. Level and output directory stay user-controlled. **Obfuscate** is enabled only when at least one included file exists.

Obfuscate on a solution session (or two or more assemblies) calls `ClosedSetProcessor`. One loose assembly keeps the current per-file path.

If the list is skip-only: Obfuscate disabled; snackbar explains that nothing on disk was found (build Release, drop again).

## Failures

| Situation | Result |
|-----------|--------|
| All skipped | No write. Reasons listed. CLI exit 2. |
| Load failure | That entry `SkipLoadFailed`; recompute set; continue if anything remains. |
| Technique/pipeline error | No output committed. Error names assembly and obfuscator. CLI exit 1. UI: included rows Error. |
| Cancel | Discard temp output. |
| Output directory not writable | Fail before any write. |

## Tests

**Analyzer** (temp folders, no `dotnet build`): `.sln` / `.slnx` / `.csproj` parsing; skip tests / missing project / missing bin; Release over Debug; WPF / AOT / Blazor / Unity hints; exe+lib vs libraries-only public API; multi-TFM only includes TFMs with files.

**Closed-set** (compile two tiny assemblies): App calls a public Lib method → still runs, Lib public name **changed**; libraries-only → public name **unchanged**; failure on second module → empty output dir; load failure → remaining set still obfuscates, report includes `SkipLoadFailed`.

**CLI:** solution input; `--dry-run` exit 0; all-skipped exit 2; two `.sln` arguments error.

**UI:** drop `.sln` expands includes + skipped; skip-only cannot obfuscate; Add Files / drop accept solution extensions.

## Out of scope (v1)

- Visual Studio and Rider solution actions
- MSBuild `.targets` injection / protect-on-every-build
- Auto restore or build
- BAML/XAML rewrite, `enum.ToString()`, Unity `Start`/`Update` scanning (v1 keeps `preserveXaml` heuristics, attribute excludes, Unity **namespaces** + `runtimeProfile`)
- Evaluating `Directory.Build.props` / arbitrary `$(Property)`
- Source obfuscation from a solution (session is assemblies only)
- Multiple solutions in one CLI run

## Implementation notes

- Prefer conventional `bin` glob over `Microsoft.Build` evaluation so Core stays SDK-host-free.
- Reuse `ConfigurationWizard.ApplyUseCaseDefaults` mappings for hint overlays; do not duplicate magic strings.
- Share one symbol map in `PipelineContext` for the session report and optional UI symbol-map export.
- Keep `IObfuscator` per-module; closed-set is a coordinator, not a new obfuscator phase enum, except that symbol renaming runs once before the per-module loop.
