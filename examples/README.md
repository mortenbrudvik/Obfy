# Obfy Examples

This folder contains example projects demonstrating various Obfy use cases.

## Examples

| Example | Description |
|---------|-------------|
| [BasicConsoleApp](BasicConsoleApp/) | Simple console app with standard obfuscation |
| [LibraryWithPublicApi](LibraryWithPublicApi/) | Class library preserving public API |
| [MsBuildIntegration](MsBuildIntegration/) | Automatic obfuscation in build process |

## Quick Start

1. Install Obfy:
   ```bash
   dotnet tool install --global Obfy
   ```

2. Navigate to an example:
   ```bash
   cd BasicConsoleApp
   ```

3. Build and obfuscate:
   ```bash
   dotnet build -c Release
   obfy bin/Release/net8.0/BasicConsoleApp.dll -c obfy.json -o bin/Release/net8.0/obfuscated/
   ```

4. Compare original vs obfuscated using a decompiler (ILSpy, dnSpy)

## Common Scenarios

### Console Application
See [BasicConsoleApp](BasicConsoleApp/) - Full obfuscation of all code.

### NuGet Library
See [LibraryWithPublicApi](LibraryWithPublicApi/) - Preserve public API, obfuscate internals.

### CI/CD Build
See [MsBuildIntegration](MsBuildIntegration/) - Integrate into your build pipeline.
