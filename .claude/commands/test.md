# Test Command

Run all test projects and report results.

## Test Projects

| Project | Type | Description |
|---------|------|-------------|
| Obfy.Tests | Unit | Core obfuscation tests |
| Obfy.Console.Tests | Integration | CLI parse + `Program.Main` process tests |
| Obfy.UI.Tests | Unit | ViewModels, startup CLI, XAML contrast |
| Obfy.ScenarioTests | Scenario | SDK compile → obfuscate → run |
| Obfy.UI.AutomationTests | UI | Locator unit tests (CI) + FlaUI (`Category=UI`) |
| Obfy.VisualStudio.Tests | Unit | VS helpers (no hive) |

Default `dotnet test` / CI skip `Category=UI` and `Category=Platform` (`Directory.Build.props`).

## Instructions

1. Run the default suite (same filter as CI):
```bash
dotnet test --verbosity normal
```

2. Optional project slices:
```bash
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj
dotnet test Tests/Obfy.ScenarioTests/Obfy.ScenarioTests.csproj
dotnet test Tests/Obfy.VisualStudio.Tests/Obfy.VisualStudio.Tests.csproj
dotnet test Tests/Obfy.UI.AutomationTests/Obfy.UI.AutomationTests.csproj --filter Category=UI
```

   Rider JVM tests (JDK 21): `./gradlew.bat test` in `Src/Obfy.Rider`.

3. Report results in a table:
   - Project name
   - Tests passed
   - Tests failed
   - Execution time

4. If any tests fail, analyze the failures and suggest fixes.

## Optional Arguments

- `--filter <name>`: Run specific test(s)
- `--coverage`: Generate coverage report

## Example Output

```
| Project | Passed | Failed | Time |
|---------|--------|--------|------|
| Obfy.Tests | 42 | 0 | 2.3s |
| Obfy.Console.Tests | 8 | 0 | 1.1s |

All tests passed!
```
