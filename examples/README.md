# Obfy Examples

This folder contains example projects demonstrating various Obfy use cases.

## Examples

| Example | Description |
|---------|-------------|
| [BasicConsoleApp](BasicConsoleApp/) | Simple console app with standard obfuscation |
| [LibraryWithPublicApi](LibraryWithPublicApi/) | Class library preserving public API |
| [MsBuildIntegration](MsBuildIntegration/) | Automatic obfuscation in build process |
| [unity](unity/) | Unity IL2CPP-safe `obfy.json` (`runtimeProfile: UnityIl2Cpp`) |
| [blazor](blazor/) | Blazor WASM `obfy.json` (`runtimeProfile: BlazorWasm`) |
| [maui](maui/) | MAUI / XAML `obfy.json` (`preserveXaml`, no method encryption) |

`unity`, `blazor`, and `maui` are config recipes, not full sample apps. Scenario tests cover them: Unity stub in the default job; Blazor WASM / NativeAOT / MAUI Windows as `Category=Platform`. See [docs/Unity.md](../docs/Unity.md) and [docs/Platforms.md](../docs/Platforms.md).

## Quick Start

1. Ensure Obfy is installed and available in your PATH.

2. Navigate to an example:
   ```bash
   cd BasicConsoleApp
   ```

3. Build and obfuscate:
   ```bash
   dotnet build -c Release
   obfy bin/Release/net10.0/BasicConsoleApp.dll -c obfy.json -o bin/Release/net10.0/obfuscated/
   ```

4. Compare original vs obfuscated using a decompiler (ILSpy, dnSpy)

## Common Scenarios

### Console Application
See [BasicConsoleApp](BasicConsoleApp/) - Full obfuscation of all code.

### NuGet Library
See [LibraryWithPublicApi](LibraryWithPublicApi/) - Preserve public API, obfuscate internals.

### CI/CD Build
See [MsBuildIntegration](MsBuildIntegration/) - Integrate into your build pipeline.
