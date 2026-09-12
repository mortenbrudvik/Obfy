# Testing Guide

## Overview

Obfy has comprehensive test coverage across all components:

| Project | Tests | Coverage |
|---------|-------|----------|
| Obfy.Tests | 152 | Core obfuscation logic |
| Obfy.Console.Tests | 92 | CLI parsing & integration |
| Obfy.UI.Tests | 59 | ViewModel unit tests |
| **Total** | **303** | |

## Test Stack

- **Framework**: xUnit 2.9.3
- **Mocking**: Moq 4.20.72
- **Assertions**: Shouldly 4.2.1
- **Coverage**: Coverlet 6.0.4

## Running Tests

### All Tests

```bash
dotnet test
```

### Specific Projects

```bash
dotnet test Tests/Obfy.Tests
dotnet test Tests/Obfy.Console.Tests
dotnet test Tests/Obfy.UI.Tests
```

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
| CommandParsingTests | 50+ | All CLI options and arguments |
| HelpOutputTests | 14 | Help text verification |
| ErrorHandlingTests | 13 | Error scenarios |
| IntegrationTests | 15 | End-to-end workflows |

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

Launches `ObfyUI.exe` and drives the live window:

- Launch and chrome (toolbar, files, settings, output, status)
- About ContentDialog (in-window overlay, not a separate window)
- Settings toggles, Protection and Assembly Merge expanders
- Add Files opens the native picker (Escape cancels)

Run: `dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj`

Location: `Tests/Obfy.UI.AutomationTests/`

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

Both `Obfy.Console.Tests` and `Obfy.UI.Tests` use `xunit.runner.json` to disable parallel test execution:

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

Tests run automatically on:

- Pull request creation
- Push to main branch
- Release builds

All tests must pass before merging.
