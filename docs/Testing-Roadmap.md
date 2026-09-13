# Testing improvements roadmap

A plan to close the gap between **technique coverage** (strong) and **real-app scenario coverage** (mostly missing). This is a testing plan, not a product-feature plan. Product items it unblocks are cited as `QT-*` / `PS-*` from [Roadmap.md](Roadmap.md).

How to run the current suite: [Testing.md](Testing.md).

---

## Current position (2026-09)

Four projects; counts live in [Testing.md](Testing.md) (655 as of 2026-09-13). That volume is real. What it proves is narrower than the docs imply.

| Layer | Quality | What it actually proves |
|-------|---------|-------------------------|
| Per-technique unit tests | Strong | Obfuscators rewrite IL on synthetic dnlib modules and tiny Roslyn snippets |
| Compile → obfuscate → run | Good | `EndToEndObfuscationTests` Roslyn-emits libraries (in-process invoke) and console exes (separate `dotnet` process). Not SDK `.csproj` builds |
| CLI parse / wizard defaults | Strong | Flags and use-case presets map to settings |
| WPF product UI (ViewModels) | Strong | Commands and bindings of *Obfy itself*, not of customer apps |
| FlaUI smoke | Local-only | Chrome exists; does not add files or obfuscate (`Category=UI`) |
| SDK projects / solutions | **Missing** | No `.csproj` / `.sln` is built, obfuscated, and run |
| IDE extensions | **Missing** | VS / Rider / VS Code have no tests |

Phase 0 (`TR-01` / `TR-02`) filters FlaUI out of default `dotnet test` / CI and resolves `ObfyUI.exe` per configuration. `TR-03` is confirming the first green GitHub Actions run after that merge (coverage report + 80% warning). Historical failure: [CI run 70](https://github.com/mortenbrudvik/Obfy/actions/runs/34748911530) — FlaUI looked for a Debug `ObfyUI.exe` after a Release build, so the coverage steps never ran. Later: [CI run 72](https://github.com/mortenbrudvik/Obfy/actions/runs/34750086309) — tests and coverage succeeded, then the sticky PR comment 403 (`Resource not accessible by integration`) skipped the summary and 80% warning.

`examples/` (console, public-API library, MSBuild AfterBuild, Unity/Blazor/MAUI JSON recipes) are manual demos, not fixtures.

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

`TR-01` and `TR-02` landed. Remaining: confirm a green `main` job with coverage artifact, step summary, and 80% warning. Same-repo PRs post a best-effort sticky comment; `main` never does.

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-01 | Trait-filter FlaUI out of default `dotnet test` / CI (`Category=UI` or equivalent). Document local-only run in [Testing.md](Testing.md). | S | High | ✅ Done (`Category=UI` + default `VSTestTestCaseFilter`) |
| TR-02 | Resolve `ObfyUI.exe` from the current build configuration and TFM, not a hardcoded Debug path. | S | High | ✅ Done (`UiExecutableLocator`) |
| TR-03 | Confirm CI is green on `main` and the coverage artifact + 80% warning actually run. | S | High | Open — waiting for first green Actions run on `main` after permissions + continue-on-error |

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
| TR-13 | **Merge happy path on real assemblies.** Two Roslyn- or SDK-built DLLs, `IAssemblyMerger.MergeAsync` **must** succeed, merged output loads and runs. Replace `AssemblyMerger_ReturnsCorrectAssemblyCount_OnSuccess` accepting failure. | S | Medium | Blocked — `MergeScenarioTests` exists; ILRepack.NETStandard 2.0.4 throws `NotSupportedException` on a net10 host |
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

These are the product `PS-*` items. Do not advertise first-class support until the corresponding test exists. After `TR-01`, new platform tests should use `[Trait("Category", "Platform")]` and skip when SDKs/workloads are absent. That trait exists in `Directory.Build.props`; no tests use it yet.

| ID | Work | Unblocks | Effort | Status |
|----|------|----------|--------|--------|
| TR-30 | Published Blazor WASM: obfuscate `_framework/*.dll` with `runtimeProfile: BlazorWasm`, then a headless/playwright or `dotnet` host smoke. | PS-03 | L | Open |
| TR-31 | NativeAOT: obfuscate **before** `dotnet publish -p:PublishAot=true`, run the native exe. Rename + strings + control flow + in-module proxies. | PS-04 | L | Open |
| TR-32 | MAUI Windows (not iOS/Android in CI): `preserveXaml`, method encryption off, app launches. CI-feasible subset of `PS-02`; iOS/Android remain a later platform job. | PS-02 (Windows subset) | L | Open |
| TR-33 | Unity: Development Player or a stripped managed assembly from a committed sample. Editor plugin is out of scope. | PS-01 | L | Open |

**Done when:** [Platforms.md](Platforms.md) / [Unity.md](Unity.md) can say “tested” for that row, not only “recipe.”

---

## Phase 4 — Tooling tests (P3, after Phase 1)

| ID | Work | Effort | Value | Status |
|----|------|--------|-------|--------|
| TR-40 | Visual Studio: unit-test `ObfuscationServiceWrapper` / settings JSON / `GetOutputAssemblyPathAsync` without a VS hive. Optional vsix integration later. | M | Medium | Open |
| TR-41 | Rider: JVM unit tests for settings + `AssemblyLocator`. | M | Low | Open |
| TR-42 | FlaUI: one path that adds a built fixture DLL and clicks Obfuscate (needs TR-02). Still local-or-trait-filtered. | M | Low | Open |
| TR-43 | Dedicated tests for `Settings.Core` validation if scenario work does not already hit it. | S | Low | Open |

Do not start TR-42 until Phase 0 and TR-10 exist — driving the UI to obfuscate needs a real input assembly.

---

## Engine gaps to close opportunistically (not a phase)

These are holes in the *existing* suite. Fold them into PRs that already touch the code, rather than a dedicated testing epic.

| ID | Gap | Suggested test |
|----|-----|----------------|
| TR-50 | Virtualization has one e2e arithmetic method, no unit file | Skip ineligible methods (leave IL unchanged); encode only `ldc.i4`/`ldarg`/`add`/`sub`/`mul`/`ret`; do not fail the run when nothing is eligible. Fail-closed on unsupported IL would be a product change (PF-08), not current behavior |
| TR-51 | Incremental cache has three e2e tests (hit, pack-fail, pack+hit regenerates launcher); no unit tests of `IncrementalCache.TryHit` | Settings change invalidates cache; input byte change invalidates |
| TR-52 | Anti-dump has no runtime process test | Leave as IL-only; a MiniDump test is not worth CI cost (see PF-19) |
| TR-53 | Source obfuscation of a multi-file directory is thin | Two `.cs` files, rename across files, recompile and run |

---

## CI shape (after Phase 0)

Default `dotnet test` already applies `VSTestTestCaseFilter` (`Category!=UI&Category!=Platform`) from `Directory.Build.props`. CI repeats the same filter explicitly:

```text
dotnet test Obfy.sln -c Release --filter "Category!=UI&Category!=Platform"
```

| Job | Filter | When |
|-----|--------|------|
| Default (every PR) | exclude `UI`, `Platform` | Always |
| Coverage | same as default | Always (80% *warning*, not a hard fail; PR comment is best-effort) |
| UI | `Category=UI` | Manual / nightly / local |
| Platform | `Category=Platform` | Nightly or workflow_dispatch; skip if workload missing |

`Obfy.ScenarioTests` runs in the default job once fixtures are small (WPF + examples). If TR-10 exceeds ~2 minutes, split it to a `Category=Scenario` job that still runs on every PR.

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
