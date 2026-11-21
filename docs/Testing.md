# Testing Guide

How to run and extend the current suite. Remaining gaps (Unity Development Player, MAUI iOS/Android) are in [Testing-Roadmap.md](Testing-Roadmap.md).

## Overview

Technique-level coverage is strong. SDK project scenarios (WPF, console, WinForms, examples, Unity stub) run in `Obfy.ScenarioTests`. Blazor WASM, NativeAOT, and MAUI Windows are `Category=Platform` (see [Testing-Roadmap.md](Testing-Roadmap.md) Phase 3).

This page gives no test counts, because they change with almost every commit. Each command below prints its own total; run it to see the current number.

| Project | Covers | Run |
|---------|--------|-----|
| Obfy.Tests | Core obfuscation logic, including ILSpy decompiler-resistance fixtures, schema contract, Settings.Core validation of `ObfySettings`, and build/CI/installer/MSIX guards (`HybridBuildTests`, `CiUiWorkflowTests`, `InstallerWebView2Tests`, `MsixPackageTests`) | `dotnet test Tests/Obfy.Tests` |
| Obfy.Console.Tests | CLI parsing (`CommandParsingTests`, `SolutionInputTests`), `Program.Main` process tests, wizard file write | `dotnet test Tests/Obfy.Console.Tests` |
| Obfy.App.Tests | The UI sessions (`ProtectionSession`, `InputSession`, `LogBuffer`, `ResultsStore`, `RunCoordinator`), the endpoint handlers, `JsonUtils`. No WPF, no WebView2 | `dotnet test Tests/Obfy.App.Tests` |
| Obfy.Interop.Tests | The bridge (`ApiBridge`, `ApiRegistry`, `ApiRegistrations`), event aggregator and publisher, `WebViewHandler`, request JSON contracts, and `TechniqueContentContractTests` | `dotnet test Tests/Obfy.Interop.Tests` |
| Obfy.UI.Tests | Shell wiring (`HybridWiringTests`), host service, startup command line | `dotnet test Tests/Obfy.UI.Tests` |
| Obfy.UI.AutomationTests | Locator unit tests (default CI), plus `Category=UI` tests: Playwright-over-WebView2 end-to-end (`WebUiTests`) and one FlaUI window smoke (`FlaUiSmokeTests`) | `dotnet test Tests/Obfy.UI.AutomationTests` for the locator tests; `--filter Category=UI` for the end-to-end cases (see below) |
| Obfy.ScenarioTests | SDK fixtures and checks (Unity stub, merge, WPF, WinForms, console, examples), plus `Category=Platform` tests (Blazor, NativeAOT, MAUI Windows) | `dotnet test Tests/Obfy.ScenarioTests` (default job); `--filter Category=Platform` for the platform tests |
| Obfy.VisualStudio.Tests | VS settings JSON, settings dialog ViewModel, CLI argv, in-place copy, output-assembly locator, CLI path locator (no VS hive) | `dotnet test Tests/Obfy.VisualStudio.Tests` |
| Web app (Vitest, `Src/WebApp`) | The page: shell and run bar, Files, Protection (including the Read more online calls), Results, the in-app Guide quick-start and its online-guide button, shared components, interop client and the `api.app.openGuide` wrapper, content | `npm run test:run` in `Src/WebApp` |

The default CI filter is `Category!=UI&Category!=Platform`, so the `Category=UI` and `Category=Platform` tests are not part of a plain `dotnet test`. Counts are xUnit cases (`[Theory]` expansions included), so a project's total is not its number of methods.

## Test Stack

- **Framework**: xUnit 2.9.3
- **Mocking**: Moq 4.20.72
- **Assertions**: Shouldly 4.3.0
- **Coverage**: Coverlet 10.0.1 (`coverage.runsettings`; product assemblies including `Obfy.VmRuntime`)

## Running Tests

Default `dotnet test` skips `Category=UI` and `Category=Platform`. The Windows CI job runs the web tests before the build and `Category=UI` after the default suite. The `Category=UI` tests need an interactive session, a built Release `ObfyUI.exe` and the WebView2 Runtime. The web tests need Node 24 only.

### Default (CI / every PR)

```bash
dotnet test
# equivalent: --filter "Category!=UI&Category!=Platform"
```

### Specific Projects

```bash
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.App.Tests
dotnet test Tests/Obfy.Interop.Tests
dotnet test Tests/Obfy.UI.Tests
dotnet test Tests/Obfy.ScenarioTests
dotnet test Tests/Obfy.VisualStudio.Tests
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj
```

The AutomationTests project still runs locator unit tests under the default filter. The Windows CI job runs the end-to-end cases with `--filter Category=UI`.

### Web app (Vitest)

The React page is tested without a browser or the host: Vitest + jsdom + Testing Library, with a mock of the bridge (`Src/WebApp/src/test/bridge.ts`, snapshots in `test/snapshots.ts`). Tests sit beside the code in `__tests__` folders.

```bash
cd Src/WebApp
npm ci            # first time, or after package-lock.json changes
npm run test:run  # once; `npm test` watches
npm run lint
```

CI runs `npm ci`, `npm run lint` and `npm run test:run` before the .NET build. `dotnet build` of `Src/Obfy.UI` does not run them; it only builds the page (`npm run build`).

### End-to-end (`Category=UI`, local, interactive desktop)

These tests launch the real `ObfyUI.exe`, so build it for the same configuration you test. The page's WebView2 is driven by Playwright over the Chrome DevTools Protocol: the app is started with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=N`, and Playwright attaches with `ConnectOverCDPAsync`. No Playwright browser download is needed. Each launch gets its own WebView2 profile (`OBFY_WEBVIEW2_USER_DATA`) and preferences (`OBFY_UI_PREFERENCES`) under a temp folder.

```bash
dotnet build Src/Obfy.UI/Obfy.UI.csproj -c Release
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj -c Release --filter Category=UI
```

`UiExecutableLocator` prefers the test configuration (Release vs Debug) and falls back to the other if that `ObfyUI.exe` is missing. A Debug exe loads the Vite dev server, so the end-to-end tests need the Release build, which serves the built page.

### Platform (Blazor WASM, NativeAOT, MAUI Windows)

Skipped by default. Blazor and NativeAOT restore extra packs on first run. MAUI skips when the workload is not installed. Unity is a committed stub and runs in the default job.

```bash
dotnet test Tests/Obfy.ScenarioTests/Obfy.ScenarioTests.csproj -c Release --filter Category=Platform
```

CI: `.github/workflows/platform.yml` (`workflow_dispatch` and weekly Monday).

### With Coverage

```bash
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage"
```

## Test Projects

### Obfy.Tests (Core)

Tests the core obfuscation engine:

- **Obfuscator tests**: Each technique (string encryption, control flow, etc.)
- **Service tests**: ObfuscationService, ReportService
- **Pipeline tests**: Execution order, context management
- **Utility tests**: NameGenerator, EncryptionHelper

Location: `Tests/Obfy.Tests/`

### Obfy.Console.Tests (CLI)

Tests the command-line interface:

| Category | Description |
|----------|-------------|
| CommandParsingTests | CLI options and arguments (parse) |
| SolutionInputTests | Solution/project and loose two-DLL closed-set |
| HelpOutputTests | Help text verification |
| ErrorHandlingTests | Error scenarios |
| WizardDefaultsTests | Use-case wizard defaults (Unity, Desktop, Blazor, MAUI, library, ASP.NET, public API) plus writing `obfy.json` |
| IntegrationParseTests | CLI parse without invoking `Program.Main` |
| IntegrationProcessTests | `Program.Main` for generate, dry-run, missing file, bad JSON, unknown level |
| DotnetToolPackTests | Packed `Obfy` nupkg / tool layout |

Key patterns:

- Uses `Program.CreateRootCommand()` for testing
- Uses `TestConsole` for capturing output
- Creates temp files for integration tests

Location: `Tests/Obfy.Console.Tests/`

### Obfy.App.Tests (sessions and handlers)

All UI behaviour lives in `Obfy.App`, so it is tested without WPF or WebView2:

| Area | Description |
|------|-------------|
| ProtectionSession | presets, Custom snapshot, profile gates, patches, config load/save, technique list |
| InputSession | file list, solution/project expansion, include flags, output and symbol-map paths, drops, mode hint |
| LogBuffer | appended lines, batching, clear |
| ResultsStore | summary, symbol tree, paging and search, preview decompile (off the calling thread), exports |
| RunCoordinator | start/cancel, blockers and their reasons, progress, closed-set/merge selection, failed-start recovery |
| Features | settings endpoint handlers |

Key patterns:

- Sessions take fakes for dialogs, file system and the obfuscation service (`Common/InputFakes.cs`)
- Regression tests for the backlog defects fixed in the lift carry their `UI-0xx` id in a comment

Location: `Tests/Obfy.App.Tests/`

### Obfy.Interop.Tests (bridge)

| Area | Description |
|------|-------------|
| ApiBridge / ApiRegistry | success and error envelopes, unknown API, bad params, handler exceptions never escape |
| ApiRegistrations | every endpoint is registered |
| ApiEventAggregator / ApiEventPublisher | events reach the page |
| RequestJsonContractTests | camelCase JSON shapes of the requests the page sends |
| TechniqueContentContractTests | `techniques.json` ids and option keys match the host's technique list |
| WebViewHandler | allowed origins, navigation guards, reload cap |

Location: `Tests/Obfy.Interop.Tests/`

### Obfy.UI.Tests (shell)

`HybridWiringTests` (the container resolves the shell, bridge and sessions), `ApplicationHostServiceTests` and `StartupArgumentsTests` in Obfy.App.Tests (files and `-o` from the command line). No window is created.

Location: `Tests/Obfy.UI.Tests/`

### Obfy.UI.AutomationTests (Playwright over WebView2, one FlaUI smoke)

Launches `ObfyUI.exe` (`Category=UI`, run after the default suite in CI):

- `WebUiTests` (Playwright): first launch shows the Files screen, toggling a technique on the Protection screen is reflected in its switch, F1 and the sidebar open the in-app Guide quick-start (its "Open the full guide online" button is checked, not clicked, so no browser starts), and obfuscating from startup arguments writes the output and shows Results
- `FlaUiSmokeTests` (FlaUI): the window opens with its title bar

Locator unit tests in the same project (no trait) resolve Debug vs Release `ObfyUI.exe` and run in the default suite.

Run them as described under [End-to-end](#end-to-end-categoryui-local-interactive-desktop).

Location: `Tests/Obfy.UI.AutomationTests/`

### Web app (`Src/WebApp`)

Vitest tests for the page: shell and run bar, Files, Protection, Results, the Guide quick-start (including the button that opens the online guide through `app.openGuide`), shared components, the interop client and content. See [Web app (Vitest)](#web-app-vitest).

### Obfy.VisualStudio.Tests

Hive-free tests of the VS extension helpers (`ObfySettingsJson`, `CliArgumentBuilder`, `OutputAssemblyLocator`, `ObfyCliLocator`, `SettingsDialogViewModel`). Linked from `Src/Obfy.VisualStudio/Services` so CI does not load the VSIX SDK.

Location: `Tests/Obfy.VisualStudio.Tests/`

### Obfy.ScenarioTests (SDK fixtures)

Compile → obfuscate → run real SDK projects. Fixtures live under `Tests/Obfy.ScenarioTests/Fixtures/` (not `examples/`). `examples/BasicConsoleApp` and `examples/LibraryWithPublicApi` are copied and run as-is so those recipes cannot bitrot.

| Test | What it proves |
|------|----------------|
| `ExampleScenarioTests` | Shipped example `obfy.json` still builds, obfuscates, and runs |
| `WpfAppTests` | `preserveXaml` + MainWindow exclusion: window constructs, `{Binding Title}` survives, non-ViewModel type is renamed |
| `WpfSolutionTests` | WPF app + class library: both outputs obfuscated, app still calls into the library |
| `ConsoleSolutionTests` | Console app + library: public API preserved on the lib |
| `WinFormsAppTests` | WinForms form constructs after obfuscation |
| `SatelliteTests` | `*.resources.dll` satellite is not rewritten; parent still loads cultures |
| `MsBuildIntegrationTests` | Example AfterBuild target invokes the CLI on Release |
| `MergeScenarioTests` | Two SDK class libraries merge (and merge+obfuscate) then ALC-invoke |
| `UnitySampleTests` | `examples/unity/obfy.json` on a stub with `UnityEngine` types (default job) |
| `BlazorWasmTests` | Publish WASM, obfuscate `_framework/*.dll`, ALC-invoke probe (`Category=Platform`) |
| `NativeAotTests` | Obfuscate IL, `PublishAot`, run native exe (`Category=Platform`) |
| `MauiWindowsTests` | `dotnet new maui` + Windows TFM when workload is present (`Category=Platform`) |

Harness: copy fixture to `%TEMP%`, `dotnet build -c Release`, obfuscate with `IObfuscationService`, `dotnet <assembly>` (WPF uses `--smoke`).

To add a fixture:

1. Create `Tests/Obfy.ScenarioTests/Fixtures/<Name>/` with a `.csproj` (and `obfy.json` if needed). Keep it out of `Obfy.sln` as a built project — it is test data.
2. Copy with `ScenarioHarness.CopyToTemp`, build with `ScenarioHarness.DotnetBuild`, obfuscate with `ScenarioHarness.ObfuscateAsync`.
3. Assert by running the output (`RunDotnet`) and/or inspecting it with dnlib.

Location: `Tests/Obfy.ScenarioTests/`

## Adding Tests

### For CLI Changes

1. Add tests to `Tests/Obfy.Console.Tests/`
2. Use `Program.CreateRootCommand()` to get testable command
3. Use `rootCommand.Parse()` for parsing tests
4. Use `rootCommand.Invoke()` for execution tests

Example:

```csharp
[Fact]
public void Parse_NewOption_ParsesCorrectly()
{
    var rootCommand = Program.CreateRootCommand();
    var parseResult = rootCommand.Parse("input.dll --new-option value");

    parseResult.Errors.ShouldBeEmpty();
    parseResult.GetValueForOption(Program.NewOption).ShouldBe("value");
}
```

### For UI Behaviour (sessions and endpoints)

1. Put the rule in a session in `Src/Obfy.App/Sessions` and test it in `Tests/Obfy.App.Tests/Sessions/` with fakes
2. For a new endpoint, also cover its registration and JSON shape in `Tests/Obfy.Interop.Tests/` (see "Adding a bridge endpoint" in [CONTRIBUTING.md](../CONTRIBUTING.md))
3. Test the page side in Vitest next to the component

### For Core Obfuscation

1. Add tests to `Tests/Obfy.Tests/`
2. Use `CreateTestAssembly()` helper for real assemblies
3. Verify IL modifications with dnlib

## Test Configuration

`Obfy.Console.Tests`, `Obfy.Interop.Tests`, `Obfy.UI.Tests`, `Obfy.UI.AutomationTests`, and `Obfy.ScenarioTests` use `xunit.runner.json` to disable parallel test execution:

```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

This prevents race conditions with static properties and shared state during tests.

## Best Practices

1. **Arrange-Act-Assert**: Structure tests clearly with these three sections
2. **One behavior per test**: Multiple asserts on that behavior are expected; do not split a compile→run scenario into one-assert tests
3. **Descriptive names**: Use `Method_Scenario_ExpectedResult` pattern
4. **Mock external dependencies**: Use Moq for services and I/O
5. **Use temp files**: Clean up after integration tests with `IDisposable`
6. **Prove run, not emit**: scenario and e2e tests should execute the obfuscated output (process or ALC invoke) when the product claims runtime behavior

## Continuous Integration

PRs and pushes to `main` run `.github/workflows/ci.yml`:

| Job | What |
|-----|------|
| `build` | Windows: Node 24, `npm ci`, `npm run lint` and `npm run test:run` in `Src/WebApp` (the .NET build then builds the page), then `dotnet test -c Release` with `Category!=UI&Category!=Platform` and `coverage.runsettings`. Coverage report + 80% **warning** (not a hard fail). Sticky PR comment is best-effort (`continue-on-error`, same-repo PRs only). The same job then runs `Category=UI`. |
| `linux-cli` | Ubuntu `dotnet test Tests/Obfy.Console.Tests` (cross-platform CLI claim). |

The `Category=UI` tests run on the Windows `build` job after the default suite. That runner's session is interactive and has the WebView2 Runtime, so they can launch `ObfyUI.exe` and attach Playwright to it over a debug port. Default `dotnet test` still skips `Category=UI`, so a local run does not open the window. Platform tests (`Category=Platform`) run from `.github/workflows/platform.yml`: NativeAOT on pull requests, and the full set on the weekly schedule and `workflow_dispatch`. A skipped platform test fails that job. `dotnet new maui` is allowed 180s because the first run restores the template pack. See [Testing-Roadmap.md](Testing-Roadmap.md).
