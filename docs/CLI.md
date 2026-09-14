# CLI Reference

Complete command-line interface reference for Obfy.

Install the CLI as a .NET tool (`dotnet tool install --global Obfy`) or via the Windows installer. The command is `obfy` in both cases. The tool is framework-dependent and needs .NET 10 on the machine.

## Synopsis

```
obfy <input>... [options]
obfy config generate [options]
obfy config wizard [options]
```

## Commands

### Root Command

Obfuscate one or more .NET assemblies, C# source files, solutions, or projects.

```bash
obfy <input>... [options]
```

**Arguments:**

| Argument | Description |
|----------|-------------|
| `<input>` | One or more input files to obfuscate (DLL, EXE, .cs, .sln, .slnx, or project files) |

**Options:**

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--output <dir>` | `-o` | Output directory for obfuscated files | Single file: sibling `{name}.obfuscated{ext}`. `--merge` without `-o`: sibling `{name}.merged{ext}`. Solution/project or two+ loose assemblies: `{dir}/obfy-out/` |
| `--config <file>` | `-c` | Path to JSON configuration file. Technique flags in the file are used as-is; `--level` is ignored when `-c` is set, and `level` inside the file is not re-applied as a preset (`obfy config generate -l …` writes fully resolved flags) | None |
| `--level <level>` | `-l` | Obfuscation level: `minimal`, `standard`, `aggressive`, `custom`. Applied only when `-c` is omitted | `standard` |
| `--string-encrypt` | | Enable string encryption | Off |
| `--control-flow` | | Enable control flow obfuscation | Off |
| `--rename` | | Enable symbol renaming | Off |
| `--anti-debug` | | Enable anti-debugging protection | Off |
| `--anti-tamper` | | Enable anti-tamper protection | Off |
| `--anti-decompiler` | | Enable anti-decompiler protection | Off |
| `--anti-dump` | | Enable anti-dump (PE wipe + in-process MiniDump hook, Windows) | Off |
| `--watermark-id <id>` | | Enable watermarking and set `watermark.id` (trimmed; whitespace-only is an error) | Off |
| `--virtualize` | | Enable limited IL virtualization for simple static int methods | Off |
| `--incremental` | | Skip re-obfuscation when input and settings are unchanged | Off |
| `--reference-proxy` | | Enable reference proxy | Off |
| `--proxy-external` | | Also proxy selected out-of-module calls (enables `--reference-proxy`; skips compiler/interop/pointer/value-type/generic/vararg/ctor signatures) | Off |
| `--encrypt-methods` | | Encrypt method IL in the PE image (Windows) | Off |
| `--no-string-encryption` | | Disable string encryption | Off |
| `--no-symbol-renaming` | | Disable symbol renaming | Off |
| `--no-control-flow` | | Disable control flow obfuscation | Off |
| `--strip-metadata` | | Remove debug metadata | Off |
| `--encrypt-resources` | | Enable resource encryption | Off |
| `--encrypt-constants` | | Enable constant encryption | Off |
| `--preserve-public` | | Preserve public API names (also forces library-mode on a closed set) | Off |
| `--merge` | | Merge all input assemblies into one before obfuscating | Off |
| `--internalize` | | With `--merge` only: make merged types internal. `--internalize false` keeps them public. A config `internalize: false` is overwritten when `--merge` is also passed | On (when merging) |
| `--map <file>` | | Output symbol mapping to file | None |
| `--report <file>` | | Generate obfuscation report (HTML or JSON based on extension) | None |
| `--dry-run` | | Analyze only, don't write output | Off |
| `--verbose` | `-v` | Enable verbose output | Off |
| `--no-logo` | | Suppress the banner | Off |
| `--version` | | Show version information | |
| `--help` | `-h`, `-?` | Show help | |

**Config-only (no CLI flags):** `packing.enabled` / `packing.rid`, `signing`, `runtimeProfile`, `dependencyEmbedding`, `symbolRenaming.preserveXaml`, `inclusions`, `watermark` (except `--watermark-id`), nested anti-decompiler junk counts, encryption algorithms, control-flow mode/intensity, naming mode, `metadata.removeAttributes`, and `metadata.stripDocumentation`. `--strip-metadata` only sets `removeDebugInfo`. `--virtualize` and `--incremental` turn those features on; `maxMethods` and cache behavior stay in the config file. There is no `--pack` flag.

**Breaking:** `packing.enabled` now emits a native win-x64 host that replaces the obfuscated PE. Set `packing.rid` to `portable` for the previous managed `{name}.launcher.exe`.

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

Without `--quick`, the wizard asks Quick vs Advanced (Quick is listed first as recommended). `--quick` skips that prompt.

- **Quick Mode** (`--quick`): Application type and protection level
- **Advanced Mode**: Steps through all configuration options interactively

**Use Case Presets:**

The wizard applies sensible defaults based on your application type:

| Use Case | Recommendation |
|----------|----------------|
| Desktop Application | Standard protection; `preserveXaml` |
| Console Application | Standard protection |
| Class Library / NuGet Package | Minimal protection; preserves public API |
| Web Application (ASP.NET) | Standard protection; excludes Route, ApiController, HttpGet/Post/Put/Delete |
| Blazor WebAssembly | Standard protection; `runtimeProfile: BlazorWasm` |
| MAUI / Mobile | Standard protection; `preserveXaml` |
| Game (Unity) | Standard protection; `runtimeProfile: UnityIl2Cpp`; excludes `UnityEngine`, `UnityEngine.*`, `Unity`, `Unity.*` |

## Examples

### Basic Usage

```bash
# Obfuscate a solution as a closed set
obfy MyApp.sln -o out/
obfy MyApp.sln --dry-run
obfy MyApp.sln Extra.dll -o out/

# Obfuscate a single DLL with standard protection
obfy MyApp.dll -o output/

# Obfuscate with aggressive protection
obfy MyApp.dll -l aggressive -o output/

# Obfuscate with minimal protection (symbol renaming only)
obfy MyApp.dll -l minimal -o output/
```

### Multiple Files

```bash
# Two or more existing assemblies (no .sln/.csproj) are a closed set so cross-assembly
# references stay consistent after rename. --merge still merges instead.
# --preserve-public, or a library-only set with no exe, keeps public names.
# Without -o, output goes to {first-assembly-dir}/obfy-out/.
obfy MyApp.dll MyLibrary.dll -o output/

# Shell glob (cmd.exe expands *.dll; PowerShell does not unless you use Get-ChildItem)
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

# Aggressive CLI set (omits opt-in --proxy-external, --watermark-id, --virtualize, --incremental)
obfy MyApp.dll --string-encrypt --control-flow --rename --anti-debug --anti-tamper --anti-decompiler --anti-dump --reference-proxy --encrypt-methods --strip-metadata --encrypt-resources --encrypt-constants -o output/

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

## Solution / project input

`obfy MyApp.sln` (or `.slnx` / `.csproj` / `.vbproj` / `.fsproj`) analyzes the ship set on disk and obfuscates included assemblies as one closed application.

- Extra `.dll` / `.exe` arguments join the closed set. Their library-mode is decided after load (`AssemblyRef`); `--dry-run` prints `After load` for extras unless `--preserve-public`.
- Source `.cs` files in a session are skipped with a warning.
- Two `.sln` / `.slnx` arguments in one invocation is an error (exit 1).
- A named extra that does not exist fails the run (exit 1), including `--dry-run`.
- `--merge` on a session with fewer than two included assemblies warns and continues as a closed set.
- Load failures omit that module, print the path and cause, write the remaining set, and exit 1.

`obfy App.dll Lib.dll` without a solution/project is still a closed set (same rename consistency as a solution drop). `--merge` still merges. Without `-o`, output is `{first-assembly-dir}/obfy-out/`.

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success (including dry-run when there is at least one included assembly and no missing extras) |
| 1 | Error (file not found, obfuscation failed, two solution files, load failures, invalid XML, etc.) |
| 2 | Solution/project session had no included assemblies (all skipped) |

## Environment

Obfy stores configuration and logs in the following locations:

| Type | Path |
|------|------|
| Obfuscation settings | The `-c` / `obfy config generate` file you choose (`obfy.json` in the current directory by default). Not auto-loaded from AppData |
| UI preferences | `%APPDATA%\Obfy\ui-preferences.json` (last output folder, symbol-map option) |
| Logs | `LocalApplicationData/Obfy/Logs/` (`%LOCALAPPDATA%\Obfy\Logs\` on Windows) |

The WPF UI, method IL encryption (`VirtualProtect`), and anti-dump MiniDump hook are Windows-only. The CLI can obfuscate assemblies on any OS; PE-level Windows protections warn or no-op off-Windows.

## See Also

- [Configuration](Configuration.md) - Full configuration file reference
- [Techniques](Techniques.md) - Obfuscation technique details
- [Advanced](Advanced.md) - Advanced usage and best practices
