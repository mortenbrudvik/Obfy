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
| `--output <dir>` | `-o` | Output directory for obfuscated files | CLI rules, with no `-o`: one file is written next to the input as `{name}.obfuscated{ext}`; `--merge` writes `{name}.merged{ext}` next to the first input; a solution or project writes `obfy-out/` next to the solution or project file; two or more loose assemblies write `obfy-out/` next to the first assembly. With `-o`, each file keeps its input name in that folder. The desktop app has its own rules (a merge there is named `*.obfuscated.*`, and a closed set goes next to the first included assembly) |
| `--config <file>` | `-c` | Path to JSON configuration file. Technique flags in the file are used as-is; `--level` is ignored when `-c` is set (a warning is printed), and `level` inside the file is not re-applied as a preset (`obfy config generate -l …` writes fully resolved flags). Key names are read case-insensitively (`StringEncryption` works as `stringEncryption`). A key that is not a setting (a typo such as `stringEncrption`, or `controlFlow.intensty`) is ignored with a warning, and the run continues | None |
| `--level <level>` | `-l` | Obfuscation level: `minimal`, `standard`, `aggressive`, `custom`. Applied only when `-c` is omitted. A number that is not a level (for example `9`) is an error | `standard` |
| `--string-encrypt` | | Enable string encryption | Off |
| `--control-flow` | | Enable control flow obfuscation | Off |
| `--rename` | | Enable symbol renaming | Off |
| `--anti-debug` | | Enable anti-debugging protection | Off |
| `--anti-tamper` | | Enable anti-tamper protection | Off |
| `--anti-decompiler` | | Enable anti-decompiler protection | Off |
| `--anti-dump` | | Enable anti-dump (PE wipe + in-process MiniDump hook, Windows) | Off |
| `--watermark-id <id>` | | Enable watermarking and set `watermark.id` (trimmed; whitespace-only is an error) | Off |
| `--virtualize` | | Enable IL virtualization of eligible methods (no EH/generics/byref/custom structs/ctors; gated off NativeAOT / IL2CPP / Blazor WASM) | Off |
| `--incremental` | | Skip re-obfuscation when input and settings are unchanged and the output is still the file the last run wrote. The cache file next to the output holds counts and hashes, never names. Single assembly or merge only; not used when the output replaces the input. `--map` rebuilds when the earlier run renamed symbols, `--report` also when it skipped items or raised warnings. Changing the output after the run (Authenticode-signing it, or a post-build step that touches it) makes the next run build it again (`docs/Configuration.md`) | Off |
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
| `--merge` | | Merge all input assemblies into one before obfuscating. A config with `assemblyMerge.enabled: true` does the same without the flag (with a single loose input it is ignored with a warning; `--merge` with a single input is an error) | Off |
| `--internalize` | | When merging: make merged types internal. `--internalize false` keeps them public. Only applied when typed: without it, `assemblyMerge.internalize` from the `-c` file is used, and `true` when there is no file | On (when merging) |
| `--map <file>` | | Output symbol mapping to file. The path must not be a file the run reads or writes (see [Map and report paths](#map-and-report-paths)) | None |
| `--report <file>` | | Generate an obfuscation report (HTML or JSON based on extension). A multi-file run includes every successful file. The same rule as `--map` applies to the path | None |
| `--dry-run` | | Analyze only, don't write output | Off |
| `--verbose` | `-v` | Enable verbose output | Off |
| `--no-logo` | | Suppress the banner | Off |
| `--version` | | Show version information | |
| `--help` | `-h`, `-?` | Show help | |

**Config-only (no CLI flags):** `packing.enabled` / `packing.rid`, `signing`, `runtimeProfile`, `dependencyEmbedding`, `symbolRenaming.preserveXaml`, `inclusions`, `watermark` (except `--watermark-id`), nested anti-decompiler junk counts, encryption algorithms, control-flow mode/intensity, naming mode, `metadata.removeAttributes`, and `metadata.stripDocumentation`. `--strip-metadata` only sets `removeDebugInfo`. `--virtualize` and `--incremental` turn those features on; `maxMethods` and cache behavior stay in the config file. There is no `--pack` flag.

**Breaking:** `packing.enabled` now emits a native win-x64 host that replaces the obfuscated PE. Set `packing.rid` to `portable` for the previous managed `{name}.launcher.exe`. Closed-set / solution / `obfy A.dll B.dll` packs each entry-point output.

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
| `--force` | `-f` | Overwrite the output file if it already exists. Without it an existing file is left untouched and the command fails with exit code 1 | Off |

The output folder is created when it does not exist. Enums are written as names (`"level": "Standard"`), as the wizard does, so the file validates against the schema.

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

# Obfuscate with minimal protection (symbol renaming and debug information removal)
obfy MyApp.dll -l minimal -o output/
```

### Multiple Files

```bash
# Two or more existing assemblies (no .sln/.csproj) are a closed set so cross-assembly
# references stay consistent after rename. --merge still merges instead.
# --preserve-public, or a library-only set with no exe, keeps public names.
# Without -o, output goes to {first-assembly-dir}/obfy-out/.
obfy MyApp.dll MyLibrary.dll -o output/

# Two or more .cs files are renamed as one compilation, so a type shared
# between files keeps one name. Without -o, output goes to {first-file-dir}/obfy-src/.
obfy A.cs B.cs -o output/

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

#### Map and report paths

The map and the report are written after the run, so a path that names a file the run needs would replace it. The CLI refuses such a command line before it reads or writes anything: it prints which option and which file, adds one problem-matcher line (an error against that file), and exits with code 1. The `--map` or `--report` path may not be:

- one of the inputs: every listed file (`.dll`, `.exe`, `.cs`, the solution or project file), and for a solution or project the project files and built outputs it lists, including the ones it skips;
- a file the run writes: each output, with `-o` or without (`{name}.obfuscated{ext}`, `{name}.merged{ext}`, `obfy-out/`, `obfy-src/`), the files packing writes beside an output (`{name}.runtimeconfig.json`, or `{name}.launcher.exe` and `{name}.launcher.runtimeconfig.json` with `packing.rid: portable`), and, for a single assembly or a merge, the incremental cache file `{output}.obfycache`;
- the file given with `-c`;
- for `--report`, the same file as `--map`.

Paths are compared as files: a different letter case on Windows, a `..` segment, and a symbolic link or junction in the file's name or in a folder above it do not hide a match. `--dry-run` refuses the same way. A `--map` or `--report` path inside the output folder under another name (`-o out --map out/map.json`) is fine. An output folder that is the input's folder (`obfy $(TargetPath) -o $(TargetDir)`) is a separate, documented in-place run and is not refused. Not covered: hard links, and a drive letter that aliases another path (`subst`, a mapped drive).

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
- A load failure stops the whole set: the CLI prints the path and cause of each assembly that failed, writes no modules (`No modules were written`), and exits 1.
- A closed-set failure that names no file (an assembly that is already protected, an output path collision, a signing key that cannot be loaded, invalid settings), "No built outputs found", two solution files, and `--merge` with fewer than two assemblies each print one problem-matcher line (`path(1,1): error: message`) besides the red text, filed against the solution or project file, or against the first input when there is none. A failure that has a line of its own per file (a load failure, a missing file) does not get a second one.

`obfy App.dll Lib.dll` without a solution/project is still a closed set (same rename consistency as a solution drop). `--merge` still merges. Without `-o`, the CLI writes `obfy-out/` next to the first assembly. For a solution or project it is `obfy-out/` next to the solution or project file. (The desktop app puts a closed set's `obfy-out` next to the first included assembly, which for a solution is usually inside its `bin` folder.)

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success (including dry-run when there is at least one included assembly and no missing extras) |
| 1 | Error (file not found, obfuscation failed, two solution files, load failures, invalid XML, a `--map` or `--report` path that would replace an input or an output, etc.) |
| 2 | Solution/project session had no included assemblies (all skipped) |

## Environment

Obfy stores configuration and logs in the following locations:

| Type | Path |
|------|------|
| Obfuscation settings | The `-c` / `obfy config generate` file you choose (`obfy.json` in the current directory by default). Not auto-loaded from AppData |
| UI preferences | `%APPDATA%\Obfy\ui-preferences.json` (last output folder and the symbol-map toggle). The desktop app clears a saved folder that no longer exists. A desktop `-o` path is used for that session and is not saved. `OBFY_UI_PREFERENCES` overrides the file path |
| Logs | `LocalApplicationData/Obfy/Logs/` (`%LOCALAPPDATA%\Obfy\Logs\` on Windows) |

The desktop app (WPF shell with WebView2), method IL encryption (`VirtualProtect`), and anti-dump MiniDump hook are Windows-only. The CLI can obfuscate assemblies on any OS; PE-level Windows protections warn or no-op off-Windows.

## See Also

- [Configuration](Configuration.md) - Full configuration file reference
- [Techniques](Techniques.md) - Obfuscation technique details
- [Advanced](Advanced.md) - Advanced usage and best practices
