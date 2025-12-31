# Obfy

Professional .NET obfuscation tool for protecting C# assemblies and source code.

## Installation

```bash
dotnet tool install --global Obfy
```

## Quick Start

```bash
# Basic obfuscation (standard protection)
obfy MyApp.dll -o output/

# Aggressive protection
obfy MyApp.dll -l aggressive -o output/

# With configuration file
obfy MyApp.dll -c obfy.json -o output/
```

## Protection Levels

| Level | Description |
|-------|-------------|
| `minimal` | Symbol renaming only |
| `standard` | String encryption + symbol renaming + metadata removal |
| `aggressive` | All protections at maximum intensity |
| `custom` | Configure via flags or config file |

## Features

- **String Encryption** - AES-256 or XOR encryption of string literals
- **Control Flow** - Switch dispatchers and opaque predicates
- **Symbol Renaming** - Types, methods, fields, properties, parameters
- **Anti-Debug** - Debugger detection and response
- **Metadata Removal** - Strip debug info and attributes

## Configuration

Generate a config file:

```bash
obfy config generate -o obfy.json
```

Example `obfy.json`:

```json
{
  "level": "aggressive",
  "stringEncryption": { "enabled": true, "algorithm": "Aes256" },
  "controlFlow": { "enabled": true, "intensity": 75 },
  "symbolRenaming": { "enabled": true, "preservePublicApi": true },
  "protection": { "antiDebug": true },
  "metadata": { "removeDebugInfo": true }
}
```

## Documentation

| Document | Description |
|----------|-------------|
| [CLI Reference](docs/CLI.md) | Complete command-line options |
| [Configuration](docs/Configuration.md) | Full JSON schema and examples |
| [Techniques](docs/Techniques.md) | How each obfuscation technique works |
| [Advanced](docs/Advanced.md) | Exclusions, best practices, troubleshooting |

## License

MIT License - see [LICENSE](LICENSE) for details.
