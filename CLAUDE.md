# Obfy - C# Obfuscation Tool

Obfy is a professional .NET obfuscation tool that protects C# assemblies and source code through various obfuscation techniques.

## Codebase Structure

```
Obfy/
├── Src/
│   ├── Obfy.Console/          # CLI entry point (System.CommandLine)
│   ├── Obfy.UI/               # WPF desktop application (Fluent Design)
│   │   ├── Views/             # XAML views and controls
│   │   ├── ViewModels/        # MVVM ViewModels
│   │   ├── Models/            # UI-specific models
│   │   ├── Services/          # UI services (dialogs, settings)
│   │   └── Converters/        # XAML value converters
│   ├── Obfy.Core/             # Core obfuscation logic
│   │   ├── DependencyInjection/  # Autofac module registration
│   │   ├── Models/            # Settings and result models
│   │   ├── Obfuscators/       # Obfuscation implementations
│   │   │   ├── Assembly/      # dnlib-based assembly obfuscators
│   │   │   └── Source/        # Roslyn-based source obfuscators
│   │   ├── Pipeline/          # Obfuscation pipeline orchestration
│   │   ├── Services/          # Core services
│   │   └── Utilities/         # Helper classes
│   ├── Obfy.VisualStudio/     # Visual Studio 2022 extension (C#)
│   ├── Obfy.Rider/            # JetBrains Rider plugin (Kotlin)
│   ├── Settings.Core/         # JSON settings persistence
│   └── Logging.Core/          # NLog logging infrastructure
├── Tests/
│   ├── Obfy.Tests/            # Core unit tests (239 tests)
│   ├── Obfy.Console.Tests/    # CLI parsing & integration tests (104 tests)
│   └── Obfy.UI.Tests/         # ViewModel unit tests (75 tests)
├── docs/                      # Documentation
├── build/                     # Build scripts and installer
│   ├── build-installer.ps1    # Installer build automation
│   ├── ObfySetup.iss          # Inno Setup script
│   └── generate-icon.ps1      # Icon generation
└── .claude/                   # Claude Code configuration
```

## Build Commands

```bash
# Build solution
dotnet build Obfy.sln

# Build release
dotnet build Obfy.sln -c Release

# Run tests
dotnet test Tests/Obfy.Tests/Obfy.Tests.csproj
dotnet test Tests/Obfy.Console.Tests/Obfy.Console.Tests.csproj
dotnet test Tests/Obfy.UI.Tests/Obfy.UI.Tests.csproj

# Run CLI
dotnet run --project Src/Obfy.Console/Obfy.Console.csproj -- --help

# Run UI
dotnet run --project Src/Obfy.UI/Obfy.UI.csproj
```

## CLI Usage

```bash
# Basic obfuscation
obfy input.dll -o output/

# With specific level
obfy input.dll -l aggressive

# With config file
obfy input.dll -c obfy.json

# Generate config
obfy config generate -o obfy.json

# Multiple files
obfy file1.dll file2.dll -o output/

# Merge assemblies into one
obfy App.dll Lib.dll --merge -o output/
```

## UI Application

The WPF desktop application provides a visual interface for obfuscation:

- **Three-panel layout**: Settings (left), Files (center), Output (bottom)
- **Drag-and-drop**: Drop .dll/.exe/.cs files directly onto the window
- **Level presets**: Minimal, Standard, Aggressive, or Custom
- **Real-time progress**: Color-coded output logs
- **Results view**: Statistics and symbol map export

Tech stack: WPF-UI 4.1.0 (Fluent Design), CommunityToolkit.Mvvm, Autofac

## Obfuscation Techniques

### Assembly Obfuscators (dnlib)

| Technique | Priority | Description |
|-----------|----------|-------------|
| StringEncryption | 10 | Encrypts string literals (XOR or AES-256) |
| ConstantEncryption | 11 | Encrypts numeric constants (int, long, float, double; XOR or AES-256) |
| ResourceEncryption | 15 | Encrypts embedded resources (XOR or AES-256) |
| AntiDebug | 18 | Injects debugger detection |
| AntiDump | 19 | Wipes PE headers in memory (Windows) |
| AntiDecompiler | 20 | Injects junk types and methods |
| AntiTamper | 22 | Verifies assembly integrity at runtime |
| ControlFlow | 30 | Switch-dispatches basic blocks and/or inserts opaque predicates |
| ReferenceProxy | 40 | Hides in-module call targets behind proxy methods |
| SymbolRenaming | 50 | Renames types, methods, fields, properties, events, namespaces |
| MetadataRemoval | 90 | Strips debug info and attributes |

### Source Obfuscators (Roslyn)

| Technique | Priority | Description |
|-----------|----------|-------------|
| SourceStringEncryption | 10 | Encrypts string literals in source code |
| SourceControlFlow | 30 | Adds opaque predicates and transforms control flow |
| SourceSymbolRenaming | 50 | Renames identifiers in source code |

> **Security note:** String/constant/resource "encryption" is *obfuscation*, not confidentiality.
> The decryption key is embedded in the output assembly and is recoverable by anyone who runs or
> inspects it — XOR trivially, AES-256 with a little more effort. These techniques raise the cost of
> casual reverse engineering; they are not a substitute for encrypting secrets that must stay secret.
> Do not ship real secrets (keys, tokens, credentials) inside an assembly and rely on obfuscation.

## Architecture

### Pipeline Pattern
Obfuscators implement `IObfuscator` and execute in priority order through `ObfuscationPipeline`.

### Strategy Pattern
Each obfuscation technique is a separate strategy that can be enabled/disabled.

### Dependency Injection
Uses Autofac with module-based registration (`ObfuscationModule`).

## Key Files

| File | Purpose |
|------|---------|
| `Obfy.Core/Obfuscators/IObfuscator.cs` | Core interface for all obfuscators |
| `Obfy.Core/Pipeline/ObfuscationPipeline.cs` | Orchestrates obfuscator execution |
| `Obfy.Core/Models/ObfySettings.cs` | Complete configuration model |
| `Obfy.Console/Program.cs` | CLI entry point |
| `Obfy.UI/Views/MainWindow.xaml` | UI main window (three-panel layout) |
| `Obfy.UI/ViewModels/MainViewModel.cs` | UI orchestration and obfuscation logic |
| `Obfy.UI/ViewModels/SettingsViewModel.cs` | UI settings binding to ObfySettings |

## Development Workflow

Always branch out in an isolated git worktree under `.worktrees/`, never on the main checkout:

```bash
git worktree add .worktrees/<branch-name> -b <branch-name>
```

1. Create the worktree and branch (command above)
2. Implement with tests in that worktree
3. Run all tests: `dotnet test`
4. Build release: `dotnet build -c Release`
5. Commit using `/commit`
6. After merge: `git worktree remove .worktrees/<branch-name>`

## Testing Requirements

- New features: Write unit tests
- Bug fixes: Write regression tests
- Before commit: All tests must pass

## Data Storage

- Settings: `%APPDATA%\Obfy\obfy.json`
- Logs: `%LOCALAPPDATA%\Obfy\Logs\`

## Code Reuse

| Utility | Purpose |
|---------|---------|
| `NameGenerator` | Generates obfuscated symbol names |
| `EncryptionHelper` | String encryption utilities |
| `PipelineContext` | Shared state across obfuscators |
