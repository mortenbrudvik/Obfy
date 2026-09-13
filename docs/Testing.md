# Testing Guide

How to run and extend the current suite. Planned gaps (real SDK/WPF solutions, platforms) are in [Testing-Roadmap.md](Testing-Roadmap.md).

## Overview

Technique-level coverage is strong. SDK project scenarios (WPF, console, WinForms, examples, Unity stub) run in `Obfy.ScenarioTests`. Blazor WASM, NativeAOT, and MAUI Windows are `Category=Platform` (see [Testing-Roadmap.md](Testing-Roadmap.md) Phase 3).

| Project | Tests | Coverage |
|---------|-------|----------|
| Obfy.Tests | 413 | Core obfuscation logic, including ILSpy decompiler-resistance fixtures and Settings.Core validation |
| Obfy.Console.Tests | 117 | CLI parsing (`IntegrationParseTests`) and process (`IntegrationProcessTests`) |
| Obfy.UI.Tests | 132 | ViewModel unit tests, startup CLI, and XAML contrast/theme checks |
| Obfy.UI.AutomationTests | 25 | 6 locator unit tests (CI) + 19 FlaUI live-window tests (`Category=UI`, local) |
| Obfy.ScenarioTests | 14 | 10 default SDK fixtures (Unity stub included) + 3 `Category=Platform` + 1 skipped merge |
| Obfy.VisualStudio.Tests | 9 | VS settings JSON, CLI args, output-assembly locator (no VS hive) |
| **Total** | **687** | Default CI (`Category!=UI&Category!=Platform`) |

## Test Stack

- **Framework**: xUnit 2.9.3
- **Mocking**: Moq 4.20.72
- **Assertions**: Shouldly 4.2.1
- **Coverage**: Coverlet 6.0.4

## Running Tests

Default `dotnet test` (and CI) skip `Category=UI` and `Category=Platform`. FlaUI needs an interactive desktop and a built `ObfyUI.exe`; it is opt-in.

### Default (CI / every PR)

```bash
dotnet test
# equivalent: --filter "Category!=UI&Category!=Platform"
```

### Specific Projects

```bash
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.UI.Tests
dotnet test Tests/Obfy.ScenarioTests
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj
```

The AutomationTests project still runs locator unit tests under the default filter. FlaUI cases stay skipped.

### FlaUI (local, interactive desktop)

Build the UI for the same configuration you test, then:

```bash
dotnet build Src/Obfy.UI/Obfy.UI.csproj -c Release
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj -c Release --filter Category=UI
```

`UiExecutableLocator` prefers the test configuration (Release vs Debug) and falls back to the other if that `ObfyUI.exe` is missing.

### Platform (Blazor WASM, NativeAOT, MAUI Windows)

Skipped by default. Blazor and NativeAOT restore extra packs on first run. MAUI skips when the workload is not installed. Unity is a committed stub and runs in the default job.

```bash
dotnet test Tests/Obfy.ScenarioTests/Obfy.ScenarioTests.csproj -c Release --filter Category=Platform
```

CI: `.github/workflows/platform.yml` (`workflow_dispatch` and weekly Monday).

### With Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
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

| Category | Tests | Description |
|----------|-------|-------------|
| CommandParsingTests | 72 | CLI options and arguments (parse) |
| HelpOutputTests | 15 | Help text verification |
| ErrorHandlingTests | 14 | Error scenarios |
| WizardDefaultsTests | 4 | Use-case wizard defaults |
| IntegrationTests | 12 | Mix of parse, config generate, and one real `Program.Main` run (see TR-14 / TR-25) |

Key patterns:

- Uses `Program.CreateRootCommand()` for testing
- Uses `TestConsole` for capturing output
- Creates temp files for integration tests

Location: `Tests/Obfy.Console.Tests/`

### Obfy.UI.Tests (ViewModels)

Tests the WPF UI ViewModels:

| ViewModel | Tests | Description |
|-----------|-------|-------------|
| SettingsViewModel | presets, conversions, exclusions, ApplyLevel parity |
| FilesViewModel | file management, drop filters, commands |
| MainViewModel | obfuscate/cancel, merge, snackbars, batch report |
| ResultsViewModel | tree grouping, search, export, copy |
| Converters | visibility, enum descriptions, status/kind icons |

Key patterns:

- Uses Moq for service dependencies
- Tests commands (CanExecute, Execute)
- Tests property change notifications
- No WPF dispatcher required (pure ViewModel logic)

Location: `Tests/Obfy.UI.Tests/`

### Obfy.UI.AutomationTests (FlaUI)

Launches `ObfyUI.exe` and drives the live window (`Category=UI`, not in default CI):

- Launch and chrome (toolbar, files, settings, output, status)
- About ContentDialog (in-window overlay, not a separate window)
- Settings toggles, Protection and Assembly Merge expanders
- Add Files opens the native picker (Escape cancels)

Locator unit tests in the same project (no trait) resolve Debug vs Release `ObfyUI.exe` and run in CI.

Run FlaUI: `dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj --filter Category=UI`

`ObfuscateFlowTests` launches `ObfyUI.exe <dll> -o <dir>` and clicks Obfuscate (no native file picker).

Location: `Tests/Obfy.UI.AutomationTests/`

### Obfy.VisualStudio.Tests

Hive-free tests of the VS extension helpers (`ObfySettingsJson`, `CliArgumentBuilder`, `OutputAssemblyLocator`). Linked from `Src/Obfy.VisualStudio/Services` so CI does not load the VSIX SDK.

Location: `Tests/Obfy.VisualStudio.Tests/`

Rider JVM tests live in `Src/Obfy.Rider/src/test/kotlin` (`./gradlew test`; JDK 21 + Rider SDK).

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

### For ViewModel Changes

1. Add tests to `Tests/Obfy.UI.Tests/ViewModels/`
2. Mock service dependencies with Moq
3. Test property changes and command behavior

Example:

```csharp
[Fact]
public void NewProperty_WhenChanged_UpdatesState()
{
    var mockService = new Mock<ISettingsService>();
    var viewModel = new MyViewModel(mockService.Object);

    viewModel.NewProperty = "value";

    viewModel.NewProperty.ShouldBe("value");
}
```

### For Core Obfuscation

1. Add tests to `Tests/Obfy.Tests/`
2. Use `CreateTestAssembly()` helper for real assemblies
3. Verify IL modifications with dnlib

## Test Configuration

`Obfy.Console.Tests`, `Obfy.UI.Tests`, `Obfy.UI.AutomationTests`, and `Obfy.ScenarioTests` use `xunit.runner.json` to disable parallel test execution:

```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

This prevents race conditions with static properties and shared state during tests.

## Best Practices

1. **Arrange-Act-Assert**: Structure tests clearly with these three sections
2. **One assertion per test**: Keep tests focused on a single behavior
3. **Descriptive names**: Use `Method_Scenario_ExpectedResult` pattern
4. **Mock external dependencies**: Use Moq for services and I/O
5. **Use temp files**: Clean up after integration tests with `IDisposable`

## Continuous Integration

PRs and pushes to `main` run `.github/workflows/ci.yml`: `dotnet test -c Release` with `Category!=UI&Category!=Platform`. The coverage report and an 80% **warning** (not a hard fail) run when tests succeed (later steps still run if the PR comment fails). The sticky PR coverage comment is best-effort (`continue-on-error`, same-repo PRs only); a 403 does not skip the summary or threshold. Pushes to `main` get the artifact, job summary, and 80% warning, not a PR comment.

FlaUI (`Category=UI`) is local-only. Platform tests (`Category=Platform`) run from `.github/workflows/platform.yml`. See [Testing-Roadmap.md](Testing-Roadmap.md) (`TR-01`–`TR-03`, `TR-30`–`TR-33`, `TR-42`).
