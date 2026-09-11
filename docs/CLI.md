# CLI Reference

Complete command-line interface reference for Obfy.

## Synopsis

```
obfy <input>... [options]
obfy config generate [options]
obfy config wizard [options]
```

## Commands

### Root Command

Obfuscate one or more .NET assemblies or C# source files.

```bash
obfy <input>... [options]
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<input>` | One or more input files to obfuscate (DLL, EXE, or .cs files) |

**Options:**

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--output <dir>` | `-o` | Output directory for obfuscated files | Same as input |
| `--config <file>` | `-c` | Path to JSON configuration file | None |
| `--level <level>` | `-l` | Obfuscation level: `minimal`, `standard`, `aggressive`, `custom` | `standard` |
| `--string-encrypt` | | Enable string encryption | Off |
| `--control-flow` | | Enable control flow obfuscation | Off |
| `--rename` | | Enable symbol renaming | Off |
| `--anti-debug` | | Enable anti-debugging protection | Off |
| `--anti-tamper` | | Enable anti-tamper protection | Off |
| `--anti-decompiler` | | Enable anti-decompiler protection | Off |
| `--anti-dump` | | Enable anti-dump protection | Off |
| `--reference-proxy` | | Enable reference proxy | Off |
| `--encrypt-methods` | | Encrypt method IL in the PE image (Windows) | Off |
| `--no-string-encryption` | | Disable string encryption | Off |
| `--no-symbol-renaming` | | Disable symbol renaming | Off |
| `--no-control-flow` | | Disable control flow obfuscation | Off |
| `--strip-metadata` | | Remove debug metadata | Off |
| `--encrypt-resources` | | Enable resource encryption | Off |
| `--encrypt-constants` | | Enable constant encryption | Off |
| `--preserve-public` | | Preserve public API names | Off |
| `--merge` | | Merge all input assemblies into one before obfuscating | Off |
| `--internalize` | | Make merged types internal (improves obfuscation) | On |
| `--map <file>` | | Output symbol mapping to file | None |
| `--report <file>` | | Generate obfuscation report (HTML or JSON based on extension) | None |
| `--dry-run` | | Analyze only, don't write output | Off |
| `--verbose` | `-v` | Enable verbose output | Off |
| `--no-logo` | | Suppress the banner | Off |
| `--version` | | Show version information | |
| `--help` | `-h`, `-?` | Show help | |

### config generate

Generate a default configuration file.

```bash
obfy config generate [options]
```

**Options:**

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--output <file>` | `-o` | Output file path | `obfy.json` |
| `--level <level>` | `-l` | Preset level for the configuration | `standard` |

### config wizard

Interactive wizard to create a configuration file. Guides you through questions about your application type, protection level, and individual settings.

```bash
obfy config wizard [options]
```

**Options:**

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--output <file>` | `-o` | Output file path | `obfy.json` |
| `--quick` | `-q` | Quick mode - just select a preset level | Off |

**Modes:**

- **Quick Mode** (`--quick`): Asks only about application type and protection level
- **Advanced Mode** (default): Steps through all configuration options interactively

**Use Case Presets:**

The wizard applies sensible defaults based on your application type:

| Use Case | Recommendation |
|----------|----------------|
| Desktop Application | Standard protection |
| Console Application | Standard protection |
| Class Library / NuGet | Minimal protection, preserves public API |
| Web Application (ASP.NET) | Standard protection, excludes route attributes |
| Game (Unity) | Aggressive protection, excludes Unity namespaces |

## Examples

### Basic Usage

```bash
# Obfuscate a single DLL with standard protection
obfy MyApp.dll -o output/

# Obfuscate with aggressive protection
obfy MyApp.dll -l aggressive -o output/

# Obfuscate with minimal protection (symbol renaming only)
obfy MyApp.dll -l minimal -o output/
```

### Multiple Files

```bash
# Obfuscate multiple assemblies
obfy MyApp.dll MyLibrary.dll -o output/

# Obfuscate all DLLs in a directory
obfy *.dll -o output/
```

### Assembly Merging

```bash
# Merge two assemblies into one and obfuscate
obfy App.dll Lib.dll --merge -o output/

# Merge with internalization disabled (keep public types)
obfy App.dll Lib.dll --merge --internalize false -o output/

# Merge multiple libraries with aggressive protection
obfy App.dll Lib1.dll Lib2.dll --merge -l aggressive -o output/
```

### Using Configuration Files

```bash
# Generate a configuration file
obfy config generate -o obfy.json

# Generate with aggressive preset
obfy config generate -l aggressive -o obfy-aggressive.json

# Use a configuration file
obfy MyApp.dll -c obfy.json -o output/
```

### Configuration Wizard

```bash
# Run the interactive wizard
obfy config wizard

# Quick mode - just select a level
obfy config wizard --quick

# Specify output path
obfy config wizard -o myproject.json
```

### Custom Protection Flags

```bash
# Enable specific protections
obfy MyApp.dll --string-encrypt --rename -o output/

# Enable all protections manually
obfy MyApp.dll --string-encrypt --control-flow --rename --anti-debug --anti-dump --reference-proxy --strip-metadata --encrypt-resources --encrypt-constants -o output/

# Enable renaming but preserve public API
obfy MyApp.dll --rename --preserve-public -o output/

# Encrypt embedded resources (configs, data files)
obfy MyApp.dll --encrypt-resources -o output/
```

### Symbol Mapping

```bash
# Generate a symbol map for debugging
obfy MyApp.dll -o output/ --map symbols.json

# The map file contains original -> obfuscated name mappings
```

### Generate Reports

```bash
# Generate HTML report with visual styling
obfy MyApp.dll -o output/ --report obfuscation-report.html

# Generate JSON report for CI/CD integration
obfy MyApp.dll -o output/ --report report.json
```

**Report Contents:**
- Summary (level, duration, total transformations)
- File information (input/output sizes, size change %)
- Transformation statistics per obfuscator
- Processing times breakdown
- Warnings (unused settings, public API changes, skipped items)

### Dry Run

```bash
# Analyze without writing output
obfy MyApp.dll --dry-run -v
```

### Verbose Output

```bash
# See detailed obfuscation progress
obfy MyApp.dll -v -o output/
```

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1 | Error (file not found, obfuscation failed, etc.) |

## Environment

Obfy stores configuration and logs in the following locations:

| Type | Path |
|------|------|
| Settings | `%APPDATA%\Obfy\obfy.json` |
| Logs | `%LOCALAPPDATA%\Obfy\Logs\` |

## See Also

- [Configuration](Configuration.md) - Full configuration file reference
- [Techniques](Techniques.md) - Obfuscation technique details
- [Advanced](Advanced.md) - Advanced usage and best practices
