# Test Command

Run all test projects and report results.

## Test Projects

| Project | Type | Description |
|---------|------|-------------|
| Obfy.Tests | Unit | Core obfuscation tests |
| Obfy.Console.Tests | Integration | CLI integration tests |

## Instructions

1. Run unit tests:
```bash
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj --verbosity normal
```

2. Run integration tests:
```bash
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj --verbosity normal
```

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
