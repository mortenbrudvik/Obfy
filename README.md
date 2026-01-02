# Obfy

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/mortenbrudvik/Obfy/actions/workflows/ci.yml/badge.svg)](https://github.com/mortenbrudvik/Obfy/actions/workflows/ci.yml)

**Professional .NET obfuscation tool for protecting C# assemblies and source code.**

Obfy helps protect your .NET applications from reverse engineering by applying multiple obfuscation techniques including string encryption, control flow obfuscation, symbol renaming, anti-debugging, and metadata removal.

## Why Obfy?

- **Easy to Use** - Single command to obfuscate your assemblies
- **Configurable** - From minimal to aggressive protection levels
- **Modern** - Built for .NET 10 with cross-platform support
- **Extensible** - JSON configuration for fine-grained control
- **IDE Integration** - Visual Studio 2022 extension with right-click obfuscation
- **Fast** - Efficient obfuscation with minimal overhead

## Installation

Download and run the Obfy installer, which adds the `obfy` command to your PATH.

## Quick Start

### Command Line

```bash
# Basic obfuscation with standard protection
obfy MyApp.dll -o output/

# Aggressive protection for maximum security
obfy MyApp.dll -l aggressive -o output/

# Using a configuration file
obfy MyApp.dll -c obfy.json -o output/

# Generate a configuration file
obfy config generate -o obfy.json

# Generate an obfuscation report
obfy MyApp.dll -o output/ --report report.html
```

### Desktop Application

Obfy also includes a modern WPF desktop application with Fluent Design:

```bash
# Run the UI
dotnet run --project Src/Obfy.UI/Obfy.UI.csproj
```

**Features:**
- Three-panel layout (Settings, Files, Output)
- Drag-and-drop file support
- Level presets with expandable advanced settings
- Real-time progress and color-coded output
- Results visualization with symbol map export

### Visual Studio Extension

For Visual Studio 2022 users, Obfy provides seamless IDE integration:

**Features:**
- Right-click project → **Obfuscate** to protect your output assembly
- Toggle **Post-Build Obfuscation** for automatic protection on each build
- **Obfy Settings** dialog with level presets (Minimal/Standard/Aggressive)
- Per-project configuration stored in `obfy.json`
- Output window integration for real-time progress
- Tools → Options → Obfy for global defaults

## Before & After

**Original code:**
```csharp
public class UserService
{
    private const string ApiKey = "sk-1234567890";

    public User GetUser(int userId)
    {
        var connection = "Server=db.example.com";
        return Database.Query(connection, userId);
    }
}
```

**After obfuscation:**
```csharp
public class _‌‍‏‎
{
    private const string _‌‍‏ = /* encrypted */;

    public _‌‍‎ _‌‍‏‪(int _‌‍‏‫)
    {
        var _‌‍‏‬ = _StringDecryptor.Decrypt(0);
        return _‌‍‎‏._‌‍‏‭(_‌‍‏‬, _‌‍‏‫);
    }
}
```

## Protection Levels

| Level | Description | Use Case |
|-------|-------------|----------|
| `minimal` | Symbol renaming only | Quick protection, debugging easier |
| `standard` | String encryption + renaming + metadata | Balanced protection (default) |
| `aggressive` | All protections at maximum | Maximum security |
| `custom` | Configure via flags or config file | Fine-tuned control |

## Features

### String Encryption
Encrypts string literals with AES-256 or XOR, making sensitive data like API keys and connection strings unreadable in the binary.

### Resource Encryption
Encrypts embedded resources (config files, data files, images) so they cannot be extracted from the assembly.

### Control Flow Obfuscation
Transforms code structure using switch dispatchers and opaque predicates, making the logic harder to follow.

### Symbol Renaming
Renames types, methods, fields, properties, and parameters to meaningless identifiers while preserving functionality.

### Anti-Debug Protection
Injects debugger detection that responds to debugging attempts, deterring runtime analysis.

### Anti-Decompiler Protection
Injects junk types and methods to clutter decompiler output, making reverse engineering more difficult.

### Metadata Removal
Strips debug information, custom attributes, and documentation, reducing attack surface and file size.

### Obfuscation Reports
Generate detailed HTML or JSON reports with statistics, file size comparison, processing times, and warnings.

```bash
obfy MyApp.dll -o output/ --report report.html
```

### Assembly Merging
Merge multiple assemblies into a single output before obfuscation. Internalize merged types for better protection.

```bash
obfy App.dll Lib1.dll Lib2.dll --merge -o output/
```

## Configuration

Create an `obfy.json` configuration file:

```json
{
  "level": "standard",
  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256"
  },
  "symbolRenaming": {
    "enabled": true,
    "preservePublicApi": true
  },
  "controlFlow": {
    "enabled": true,
    "intensity": 50
  },
  "metadata": {
    "removeDebugInfo": true
  }
}
```

## Examples

Check out the [examples](examples/) folder:

| Example | Description |
|---------|-------------|
| [BasicConsoleApp](examples/BasicConsoleApp/) | Simple console app obfuscation |
| [LibraryWithPublicApi](examples/LibraryWithPublicApi/) | Preserve public API while obfuscating internals |
| [MsBuildIntegration](examples/MsBuildIntegration/) | Automatic obfuscation in build process |

## Documentation

| Document | Description |
|----------|-------------|
| [CLI Reference](docs/CLI.md) | Complete command-line options |
| [Configuration](docs/Configuration.md) | Full JSON schema and examples |
| [Techniques](docs/Techniques.md) | How each obfuscation technique works |
| [Advanced](docs/Advanced.md) | Exclusions, best practices, troubleshooting |
| [Roadmap](docs/Roadmap.md) | Feature roadmap and backlog |

## Build Integration

### MSBuild

```xml
<Target Name="Obfuscate" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec Command="obfy $(TargetPath) -c obfy.json -o $(TargetDir)" />
</Target>
```

### GitHub Actions

```yaml
- name: Obfuscate
  run: obfy bin/Release/net10.0/MyApp.dll -l aggressive -o dist/
```

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Security

For security issues, please see [SECURITY.md](SECURITY.md).

## License

MIT License - see [LICENSE](LICENSE) for details.

---

**Made with ❤️ for the .NET community**
