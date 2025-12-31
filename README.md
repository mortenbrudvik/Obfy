# Obfy

Professional .NET obfuscation tool for protecting C# assemblies and source code.

## Installation

```bash
# Install as a global tool
dotnet tool install --global Obfy

# Or install locally in a project
dotnet new tool-manifest
dotnet tool install Obfy
```

## Usage

```bash
# Basic obfuscation
obfy input.dll -o output/

# With specific protection level
obfy input.dll -l aggressive -o output/

# With configuration file
obfy input.dll -c obfy.json -o output/

# Multiple files
obfy file1.dll file2.dll -o output/
```

## Protection Levels

| Level | Description |
|-------|-------------|
| `minimal` | Symbol renaming only |
| `standard` | String encryption + symbol renaming + metadata removal |
| `aggressive` | All protections enabled at maximum intensity |
| `custom` | Configure individual settings via flags or config file |

## Features

### Assembly Obfuscation (dnlib)
- **String Encryption** - Encrypts string literals with AES-256 or XOR
- **Control Flow** - Flattens control flow with switch dispatchers
- **Symbol Renaming** - Renames types, methods, fields, properties
- **Anti-Debug** - Injects debugger detection checks
- **Metadata Removal** - Strips debug info and custom attributes

### Source Code Obfuscation (Roslyn)
- **String Encryption** - Encrypts string literals in source code
- **Symbol Renaming** - Renames identifiers using semantic analysis
- **Control Flow** - Transforms control flow with opaque predicates

## CLI Options

```
obfy <input> [options]

Arguments:
  <input>  Input file(s) to obfuscate

Options:
  -o, --output <dir>       Output directory
  -l, --level <level>      Protection level (minimal|standard|aggressive|custom)
  -c, --config <file>      Configuration file path
  --string-encrypt         Enable string encryption
  --control-flow           Enable control flow obfuscation
  --rename                 Enable symbol renaming
  --anti-debug             Enable anti-debugging
  --strip-metadata         Remove debug metadata
  --preserve-public        Preserve public API names
  -v, --verbose            Enable verbose output
  --version                Show version
  -?, -h, --help           Show help
```

## Configuration File

Create `obfy.json`:

```json
{
  "level": "aggressive",
  "stringEncryption": {
    "enabled": true,
    "algorithm": "Aes256"
  },
  "controlFlow": {
    "enabled": true,
    "mode": "SwitchDispatcher",
    "intensity": 75
  },
  "symbolRenaming": {
    "enabled": true,
    "mode": "Unreadable",
    "preservePublicApi": true
  },
  "antiDebug": {
    "enabled": true
  },
  "metadataRemoval": {
    "enabled": true,
    "removeDebugInfo": true
  }
}
```

## License

MIT License - see [LICENSE](LICENSE) for details.
