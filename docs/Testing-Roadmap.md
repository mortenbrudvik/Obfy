# Testing improvements roadmap

Technique coverage is strong; SDK and platform scenarios now exist (see Current position). Remaining: Unity Development Player, MAUI iOS/Android, VS hive / Rider UI. This is a testing plan, not a product-feature plan. Product items it unblocks are cited as `QT-*` / `PS-*` from [Roadmap.md](Roadmap.md).

How to run the current suite: [Testing.md](Testing.md).

---

## Current position (2026-09)

Six projects; counts live in [Testing.md](Testing.md). That volume is real. What it proves is narrower than a raw count implies.

| Layer | Quality | What it actually proves |
|-------|---------|-------------------------|
| Per-technique unit tests | Strong | Obfuscators rewrite IL on synthetic dnlib modules and tiny Roslyn snippets |
| Compile → obfuscate → run | Good | `EndToEndObfuscationTests` (Roslyn snippets) plus `Obfy.ScenarioTests` SDK fixtures |
| CLI parse / wizard defaults | Strong | Flags and use-case presets map to settings |
| WPF product UI (ViewModels) | Strong | Commands and bindings of *Obfy itself*, not of customer apps |
| FlaUI smoke | Local-only | Chrome plus `ObfuscateFlowTests` (command-line DLL, click Obfuscate, non-empty output file). `Category=UI`. |
| SDK projects / solutions | Good | WPF, console+lib, WinForms, examples, satellites, MSBuild AfterBuild |
| Platform recipes | Partial | Unity stub in default CI; Blazor WASM / NativeAOT / MAUI Windows are `Category=Platform` |
| IDE extensions | Partial | VS helpers + Rider JVM unit tests; no VS hive / Rider UI tests |

Phase 0 (`TR-01` / `TR-02`) filters FlaUI out of default `dotnet test` / CI and resolves `ObfyUI.exe` per configuration. `TR-03` is ✅ Done (coverage report + 80% warning on GitHub Actions). Historical failure: [CI run 70](https://github.com/mortenbrudvik/Obfy/actions/runs/34748911530) — FlaUI looked for a Debug `ObfyUI.exe` after a Release build, so the coverage steps never ran. Later: [CI run 72](https://github.com/mortenbrudvik/Obfy/actions/runs/34750086309) — tests and coverage succeeded, then the sticky PR comment 403 (`Resource not accessible by integration`) skipped the summary and 80% warning.

`examples/` are user-facing demos; `BasicConsoleApp`, `LibraryWithPublicApi`, and MSBuild AfterBuild are also CI fixtures (`TR-12` / `TR-22`).

The product roadmap already refuses first-class Unity / MAUI / Blazor / NativeAOT claims until compile → obfuscate → run projects exist (`PS-01`–`PS-04`). This document is the testing path to those claims.

---

## Principles

1. **Keep the engine tests.** Do not replace `AssemblyObfuscatorTests` or `EndToEndObfuscationTests` with SDK fixtures. Synthetic modules are the right regression net for IL. Scenario tests catch a different class of bug (XAML names, project references, SDK-generated code).
2. **Prove run, not just emit.** A scenario test is not done until the obfuscated output executes (process or in-process invoke). Metadata inspection alone is a unit test.
3. **One real project per target, not a matrix of TFMs.** Prefer a small `net10.0` / `net10.0-windows` fixture over combinatorial frameworks until a bug demands otherwise.
4. **CI must stay green.** Slow or desktop-interactive tests are trait-filtered, not allowed to fail Release.
5. **Do not grow FlaUI, ViewModel, or dnlib-stub counts** until the scenario harness exists. Those layers are already dense relative to product risk.

---

## Priority

| Priority | Meaning | Target |
|----------|---------|--------|
| **P0** | CI is lying; fix before adding tests | Immediate |
| **P1** | Smallest scenario harness that would have caught a WPF/solution break | Next testing slice |
| **P2** | Fill adjacent SDK shapes and tighten existing “integration” tests | Following slice |
| **P3** | Platform recipes → tested support; IDE tests | When claiming those platforms / after P1 |

Effort is relative to this repo (S ≤ 1 day, M a few days, L a week-plus including CI time).

---

## Phase 0 — Make CI honest (P0)

Phase 0 is done (`TR-01`–`TR-03`). Default CI is green on `main` with a coverage artifact, job summary, and 80% warning. Same-repo PRs post a best-effort sticky comment; `main` never does.

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-01 | Trait-filter FlaUI out of default `dotnet test` / CI (`Category=UI` or equivalent). Document local-only run in [Testing.md](Testing.md). | S | High | ✅ Done (`Category=UI` + default `VSTestTestCaseFilter`) |
| TR-02 | Resolve `ObfyUI.exe` from the current build configuration and TFM, not a hardcoded Debug path. | S | High | ✅ Done (`UiExecutableLocator`) |
| TR-03 | Confirm CI is green on `main` and the coverage artifact + 80% warning actually run. | S | High | ✅ Done ([CI run 79](https://github.com/mortenbrudvik/Obfy/actions/runs/34753072894); 80% is a warning, not a hard fail) |

**Done when:** a Release `dotnet test` of the solution on GitHub Actions passes; coverage artifact uploads; job step summary and 80% warning run; sticky PR comment is optional (same-repo, best-effort); FlaUI still runs locally with one documented command (see [Testing.md](Testing.md)).

---

## Phase 1 — Scenario harness (P1)

The missing product proof: obfuscating a **built SDK output** still runs. This is the slice to do first after CI is green.

### Fixture layout (proposed)

Keep fixtures out of `examples/` (those stay user-facing). Suggested tree:

```
Tests/Obfy.ScenarioTests/
  Fixtures/
    WpfApp/                 # UseWPF exe, bindings, a bindable type *not* named *ViewModel
    WpfSolution/            # WpfApp + class library project reference
  ScenarioTestBase.cs       # dotnet build → obfy / IObfuscationService → run
  WpfAppTests.cs
  WpfSolutionTests.cs
```

Build fixtures with `dotnet build -c Release` in a temp copy (or `OutputPath` under TestResults) so the repo tree stays clean. Invoke the real pipeline (`IObfuscationService` or `obfy` from the built CLI), not a mocked handler.

### Items

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-10 | **WPF app fixture.** Window with `{Binding}`, `x:Name`, a public property on a type that does **not** match `LooksLikeXamlBindable`’s `*ViewModel`/`*View` suffix (or an extra type that does). Aggressive or Standard rename + `preserveXaml`. Headless or process-start smoke: window constructs, bound value is readable. | M | High | ✅ Done (`WpfAppTests`, MainWindow excluded for BAML) |
| TR-11 | **WPF + class library solution.** App references a library; both outputs obfuscated (app with rename, library with `preservePublicApi` or as a private impl). App still calls into the library. | M | High | ✅ Done (`WpfSolutionTests`) |
| TR-12 | **Promote `examples/BasicConsoleApp` and `examples/LibraryWithPublicApi` to CI.** Build, obfuscate with their `obfy.json`, run / invoke. Fail if the example recipe bitrots. | S | High | ✅ Done (`ExampleScenarioTests`) |
| TR-13 | **Merge happy path on real assemblies.** Two Roslyn- or SDK-built DLLs, `IAssemblyMerger.MergeAsync` **must** succeed, merged output loads and runs. Replace `AssemblyMerger_ReturnsCorrectAssemblyCount_OnSuccess` accepting failure. | S | Medium | ✅ Done (`MergeScenarioTests` + `ILRepack.Lib` 2.0.48; `MergeAndObfuscateAsync` fails closed when merge fails) |
| TR-14 | **CLI dry-run is a real handler test.** Stop using `SetupMainHandler` no-op for tests that claim integration. Keep parse-only tests separate. | S | Medium | ✅ Done |
| TR-23 | Unit tests for `LooksLikeXamlBindable`: `*ViewModel` suffix, `INotifyPropertyChanged`, `DependencyProperty` field, `DependencyObject` base, `*View` false-positive (`Overview`/`Preview`), negative case (plain public DTO). Does not need the SDK harness. | S | Medium | ✅ Done (`ObfuscatorHelpersTests`; suffix match is ordinal-ignore-case) |

**Done when:** a WPF solution and the two examples are obfuscated and executed on CI; merge success is asserted; `docs/Testing.md` describes how to add a fixture.

### What TR-10 must catch (and current tests cannot)

- `x:Name` / `{Binding Title}` after rename
- `ICommand` and public properties on types **not** named `*ViewModel`
- BAML / `InitializeComponent` still resolving
- Generated `*.g.cs` / `pack://` resources left usable

Unit-test the `LooksLikeXamlBindable` heuristic in the same slice if it is cheaper than a full WPF window (see TR-23); do not treat that as a substitute for TR-10.

---

## Phase 2 — Adjacent SDK shapes (P2)

Only after Phase 1 is green. Same harness, more fixtures.

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-20 | Console app + class library solution (no WPF). Default rename on internals, public API preserved on the library. | S | Medium | ✅ Done (`ConsoleSolutionTests`) |
| TR-21 | WinForms exe smoke (one form, one event handler). | S | Low | ✅ Done (`WinFormsAppTests`) |
| TR-22 | `examples/MsBuildIntegration` AfterBuild in CI (`dotnet build -c Release` with `obfy` on PATH or `$(ObfyCli)`). | S | Medium | ✅ Done (`MsBuildIntegrationTests`; CLI now reads camelCase `level`) |
| TR-24 | Satellite / `*.resources.dll` skip: one fixture or a built satellite; confirm default exclude leaves it unobfuscated and the parent still loads. | S | Low | ✅ Done (`SatelliteTests`) |
| TR-25 | Split CLI “integration” file: parse vs process. Rename tests that only parse so the file list stops over-claiming. | S | Low | ✅ Done (`IntegrationParseTests` / `IntegrationProcessTests`) |

**Out of Phase 2 on purpose:** ASP.NET, worker services, mixed-mode, netframework TFMs. Add those when a bug or a claimed platform requires them.

---

## Phase 3 — Platform claims (P3)

These are the product `PS-*` items. Do not advertise first-class support until the corresponding test exists. After `TR-01`, new platform tests should use `[Trait("Category", "Platform")]` and skip when SDKs/workloads are absent.

| ID | Work | Unblocks | Effort | Status |
|----|------|----------|--------|--------|
| TR-30 | Published Blazor WASM: obfuscate `_framework/*.dll` with `runtimeProfile: BlazorWasm`, then a headless/playwright or `dotnet` host smoke. | PS-03 | L | ✅ Done (`BlazorWasmTests`; ALC invoke of a probe type; `Category=Platform`) |
| TR-31 | NativeAOT: obfuscate **before** `dotnet publish -p:PublishAot=true`, run the native exe. Rename + strings + control flow + in-module proxies. | PS-04 | L | ✅ Done (`NativeAotTests`; ILC stdout + native exe; `Category=Platform`) |
| TR-32 | MAUI Windows (not iOS/Android in CI): obfuscates Windows TFM output; does not launch the app. CI-feasible subset of `PS-02`; iOS/Android remain a later platform job. | PS-02 (Windows subset) | L | ✅ Done (`MauiWindowsTests`; `dotnet new maui` when workload is present; skips otherwise) |
| TR-33 | Unity: Development Player or a stripped managed assembly from a committed sample. Editor plugin is out of scope. | PS-01 | L | ✅ Done (`UnitySampleTests`; committed stub assembly; runs in the default job) |

**Done when:** [Platforms.md](Platforms.md) / [Unity.md](Unity.md) can say “tested” for that row, not only “recipe.”

---

## Phase 4 — Tooling tests (P3, after Phase 1)

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-40 | Visual Studio: unit-test `ObfuscationServiceWrapper` / settings JSON / `GetOutputAssemblyPathAsync` without a VS hive. Optional vsix integration later. | M | Medium | ✅ Done (`Obfy.VisualStudio.Tests`: JSON, CLI args, `OutputAssemblyLocator`, `ObfyCliLocator`; wrapper process still needs a hive/CLI) |
| TR-41 | Rider: JVM unit tests for settings + `AssemblyLocator`. | M | Low | ✅ Done (`src/test/kotlin`; `./gradlew test` needs JDK 21 + Rider SDK) |
| TR-42 | FlaUI: one path that adds a built fixture DLL and clicks Obfuscate (needs TR-02). Still local-or-trait-filtered. | M | Low | ✅ Done (`ObfuscateFlowTests`; command-line DLL + Obfuscate; `Category=UI`) |
| TR-43 | Dedicated tests for `Settings.Core` validation if scenario work does not already hit it. | S | Low | ✅ Done (`SettingsValidatorTests`) |

Do not start TR-42 until Phase 0 and TR-10 exist — driving the UI to obfuscate needs a real input assembly.

---

## Phase 5 — Engine-gap unit tests

The items that were listed as opportunistic holes. TR-52 stays IL-only (PF-19).

| ID | Work | Status |
|----|------|--------|
| TR-50 | Virtualization: skip ineligible IL unchanged; encode `ldc.i4`/`ldarg`/`add`/`sub`/`mul`/`ret`; succeed when nothing is eligible | ✅ Done (`VirtualizationObfuscatorTests`) |
| TR-51 | `IncrementalCache.TryHit`: settings change and input-byte change invalidate; packing without runtimeconfig (win-x64) or launcher (portable) is a miss | ✅ Done (`IncrementalCacheTests`) |
| TR-52 | Anti-dump runtime MiniDump | Left as IL-only (see PF-19) |
| TR-53 | Two `.cs` files, rename across files, recompile and run | ✅ Done (`SourceDirectory_TwoFiles_RenameAcrossFiles_RecompilesAndRuns`) |

**Done when:** TR-50, TR-51, and TR-53 have passing tests on the default CI filter.

---

## CI shape (after Phase 0)

Default `dotnet test` already applies `VSTestTestCaseFilter` (`Category!=UI&Category!=Platform`) from `Directory.Build.props`. CI repeats the same filter explicitly:

```text
dotnet test Obfy.sln -c Release --filter "Category!=UI&Category!=Platform"
```

| Job | Filter | When |
|-----|--------|------|
| Default (every PR) | exclude `UI`, `Platform` | Always (`ci.yml` `build`) |
| Coverage | same as default | Always (80% *warning*, not a hard fail; PR comment is best-effort) |
| Linux CLI | `Obfy.Console.Tests` | Always (`ci.yml` `linux-cli` on `ubuntu-latest`) |
| Rider | Gradle `test` | Always (`ci.yml` `rider-tests`) |
| UI | `Category=UI` | Local only (no nightly workflow) |
| Platform | `Category=Platform` | `.github/workflows/platform.yml` (weekly Monday + `workflow_dispatch`); MAUI skips if the workload is missing |

`Obfy.ScenarioTests` (minus `Category=Platform`) runs in the default job. There is no separate `Category=Scenario` job.

---

## Explicit non-goals

- Replacing dnlib unit tests with SDK projects
- Combinatorial TFM matrix (`net48` × `net8` × `net10`) without a demonstrated bug
- Automating ProcDump / external anti-dump (PF-19)
- A general virtualization VM test suite (PF-08) before the VM exists
- Growing FlaUI page-object coverage of every settings toggle
- Claiming Unity / MAUI / Blazor / AOT support from wizard-default tests alone

---

## Success metrics

| Phase | KPI |
|-------|-----|
| 0 | `main` CI green; coverage artifact + step summary + 80% warning run; same-repo PR comment is best-effort |
| 1 | At least one WPF solution and two examples compile → obfuscate → run on CI |
| 2 | Console+lib and MSBuild AfterBuild on CI; merge success asserted |
| 3 | Each claimed platform has one compile → obfuscate → run job (possibly nightly) |
| 4 | VS wrapper / `GetOutputAssemblyPathAsync` covered without a VS instance |
| 5 | Virtualization skip/encode, `IncrementalCache.TryHit`, and two-file source rename all on the default CI filter |

---

## Implementation order

Do not parallelize Phase 1 across many fixtures until the harness (`ScenarioTestBase`, fixture copy/build, obfuscate, run, cleanup) exists once.

```text
TR-03                    # confirm GitHub Actions green
TR-10 harness + WpfApp   # first scenario
TR-11 WpfSolution
TR-12 examples in CI
TR-13, TR-14, TR-23      # cheap correctness; TR-23 does not need the SDK harness
TR-20, TR-21, TR-22, TR-24, TR-25  # Phase 2
TR-30 … TR-33            # only when claiming the platform
TR-40 … TR-43            # tooling
TR-50, TR-51, TR-53      # engine gaps; TR-52 stays IL-only
```

---

## Documentation to update as items land

| File | When |
|------|------|
| [Testing.md](Testing.md) | New project, traits, how to add a fixture, test counts |
| This file | Flip Status to Done; do not leave stale Open rows |
| [Roadmap.md](Roadmap.md) | `PS-01`–`PS-04` when the matching TR-3x test exists |
| [Platforms.md](Platforms.md) / [Unity.md](Unity.md) | “Recipe” → “tested” only after TR-30–TR-33 |
| `CONTRIBUTING.md` / `CLAUDE.md` | Test project list and `dotnet test` filter |
