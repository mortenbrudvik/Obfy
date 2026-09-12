# Changelog

All notable changes to Obfy will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- MSIX packaging for Microsoft Store / sideload (`build/build-msix.ps1`)
- Anti-dump PE-header wipe at module load (Aggressive preset)
- Reference proxy for in-module method calls (Aggressive preset)
- Native `IsDebuggerPresent` check in addition to managed debugger APIs
- `calli` reference proxies (ILSpy cannot inline a `call`+`ret` trampoline)
- XOR-encoded string decrypt indices so call sites are not `Decrypt(0)`, `Decrypt(1)`, …
- Method IL encryption: XOR method bodies in the PE and decrypt them at module load (Aggressive)
- Symbol renaming for events, accessors, and namespaces
- CFG control-flow flattening for methods with branches
- Opaque predicates based on `Environment.TickCount` instead of foldable constants
- CLI flags `--anti-dump`, `--reference-proxy`, `--encrypt-constants`, `--no-control-flow`
- JSON Schema for `obfy.json` (`schemas/obfy.schema.json`); `obfy config generate` writes `$schema`
- Directory.Build.props, `.editorconfig`, and `version.json`

### Changed
- String and constant encryption now cover try/catch, compiler-generated methods, and compiler-generated types (async/iterator/lambda display classes)
- Control flow applies opaque predicates to methods with exception handlers (`using`/`await`/`try`) instead of skipping them
- Opaque predicates use several always-true forms plus a junk dead branch, not only `n*(n+1)%2`
- Anti-dump wipes MZ, checksum, import, debug, and IAT directory slots (not only PE checksum)
- Anti-tamper falls back to `Environment.ProcessPath` when `Assembly.Location` is empty; `Verify` is assembly-visible
- Anti-tamper magic marker is no longer the ASCII string `OBFY_AT_MAGIC!!`
- Resource-string encryption prefixes ciphertext so `ResourceManager.GetString` leaves short/plaintext values readable
- IDE `obfy.json` uses the same nested schema as the CLI
- VS Obfuscate writes into the project output directory; Rider post-build actually runs after a successful build
- WPF app bootstraps with `IHost`, follows the system theme, and uses Fluent snackbar/content dialogs
- Runtime helpers are renamed and use encoded keys; ciphertext is stored as byte arrays
- Hash naming is salted per run so original names are not a dictionary oracle
- Resource encryption rewrites `GetManifestResourceStream(Type, string)` and `Module` overloads
- Anti-debug also checks `Debugger.IsLogging()`
- Junk types no longer live in the `Obfy.Internal` namespace

### Fixed
- `exclusions.Methods` is honored by assembly symbol renaming
- `encryptConstantStrings` / `encryptResourceStrings` settings now take effect
- Source interpolated string parts are encrypted
- Obfuscation no longer mutates the caller's settings object when applying a level preset
- Aggressive and AES obfuscation now produce loadable, runnable assemblies (constant decryptors, empty-stack control-flow splits, crypto types referenced from their real assemblies)
- Source switch flattening no longer emits CS0161, wraps declarations, or splits `out var` / local functions across cases
- Source symbol renaming is semantic (ISymbol), including attributes and record positional properties
- String/constant encryption and control-flow record skipped try/catch methods instead of silently leaving them unprotected
- Resource encryption decrypts only encrypted names, rewrites only `Assembly.GetManifestResourceStream(string)`, and warns on other load paths
- Control-flow fails the run instead of returning success after a half-rewritten method body, and does not split IL prefixes
- Anti-debug warns when no call site could be instrumented; anti-tamper fail-closes if the hash blob is missing
- Skips and warnings are shown in CLI/UI output and exported reports, not only in `--report`
- UI overall status reports file failures
- Assembly string encryption now decrypts at runtime (AES-256 and XOR) instead of returning ciphertext
- Anti-tamper hash excludes the stored digest so Aggressive builds no longer exit on startup
- Resource encryption keeps embedded resources and unwraps `GetManifestResourceStream`
- Custom CLI flags, UI toggles, and IDE settings are no longer overwritten by level presets
- CLI exits with code 1 and does not print success when obfuscation fails
- Added `--anti-tamper`, `--anti-decompiler`, `--no-string-encryption`, and `--no-symbol-renaming` so VS/Rider Custom mode matches the CLI
- Symbol renaming no longer breaks virtuals or implicit interface implementations
- Source obfuscation no longer rewrites attribute arguments or leaves constructors unrenamed
- Source switch flattening skips methods with locals/`break`/`continue`
- Missing config files, invalid `--level`, and `--merge` with one file now error instead of silently continuing
- CI runs on Windows so WPF projects can build
- Resource-string decrypt hook no longer `FromBase64String`s unencrypted short strings (would throw at runtime)
- UI last-output-directory / symbol-map preferences are saved and restored
- VS/Rider Custom mode can enable anti-dump, reference proxy, and constant encryption
- VS Generate Symbol Map option now passes `--map`

## [1.2.0] - 2026-01-21

### Added
- JetBrains Rider plugin for IDE integration (DX-04)
  - Right-click "Obfuscate" command on projects
  - Tool window with obfuscation output
  - Settings panel with level presets
- UI automation tests with FlaUI (QT-04)
  - Test project: Tests/Obfy.UI.AutomationTests
  - FlaUI.UIA3 framework for WPF automation
  - AutomationIds added to all key XAML controls
  - Tests for application launch, settings panel, about dialog
  - Page object model with MainWindowElements helper
  - Local-only execution (requires interactive Windows desktop)
- Code coverage CI integration (QT-03)
  - Coverage collection with Coverlet during CI builds
  - HTML coverage reports via ReportGenerator
  - Coverage summary in GitHub Actions job summary
  - PR comments with coverage details
  - Coverage report artifacts downloadable from CI
  - CI status badge in README
- Comprehensive test coverage for CLI and UI (QT-01, QT-02)
  - 92 CLI tests: argument parsing, help output, error handling, integration
  - 59 UI ViewModel tests: FilesViewModel, SettingsViewModel commands
  - New Obfy.UI.Tests project with xUnit, Moq, Shouldly
  - Testing documentation guide at docs/Testing.md
- Self-obfuscation in build process to protect Obfy's own assemblies
  - Obfuscates CLI and UI assemblies before packaging into installer
  - Uses copy-then-obfuscate approach to solve bootstrap problem
  - Applies string encryption, symbol renaming, and metadata removal
  - New `-SkipObfuscation` flag for build-installer.ps1
- Configuration Wizard for interactive config generation (DX-05)
  - `obfy config wizard` command with Quick and Advanced modes
  - Quick mode: Select application type and protection level
  - Advanced mode: Step through all settings interactively
  - Use case presets for Desktop, Library, Web, Console, Unity
  - Spectre.Console rich prompts and summary table
- Visual Studio 2022 Extension for IDE integration (DX-02)
  - Right-click "Obfuscate" command on projects
  - Toggle post-build obfuscation per-project
  - Settings dialog with level presets
  - Output window integration
- Assembly merging to combine multiple DLLs into one (UF-01)
  - Merge multiple assemblies before obfuscation for single-file output
  - Internalize merged types (makes public types internal for better obfuscation)
  - Exclude patterns for framework assemblies (System.*, Microsoft.*)
  - `--merge` and `--internalize` CLI options
  - Configurable via `assemblyMerge` settings in obfy.json
- Anti-decompiler protection to make reverse engineering harder (PF-04)
  - Injects junk types and methods to clutter decompiler output
  - Adds SuppressIldasm attribute to block ILDasm
  - Configurable junk type count and methods per type
  - Uses confusing Unicode characters for junk names

### Fixed
- Exclude Obfy.Core.Models namespace from self-obfuscation
- Rider plugin CLI path quoting and blank tool window

## [1.1.0] - 2026-01-01

### Added
- WPF desktop application with Fluent Design (DX-01)
  - Three-panel layout (Settings, Files, Output)
  - Drag-and-drop file support
  - Level presets with expandable advanced settings
  - Real-time progress and color-coded output
  - Results visualization with symbol map export
- Anti-tamper detection to verify assembly integrity at runtime (PF-03)
  - Computes SHA-256 hash of method bodies during obfuscation
  - Injects runtime verification at entry point and/or module initializer
  - Graceful fallback for single-file published apps
  - Configurable via `protection.antiTamper` settings
- Resource encryption obfuscator to encrypt embedded resources (PF-01)
- Constant encryption obfuscator to encrypt numeric literals (PF-02)
  - Supports int, long, float, double constants
  - Configurable thresholds to skip small/common values
  - XOR encryption for fast runtime decryption
- Obfuscation report generation (UF-04)
  - Generate detailed HTML reports with visual styling
  - Generate JSON reports for machine processing
  - `--report <file>` CLI option (format based on extension)
  - "Export Report" button in UI results panel
  - Reports include: statistics, file size comparison, processing times, warnings
  - Warnings for unused settings, skipped items, and public API changes
- `--encrypt-resources` CLI option
- Pattern-based include/exclude for resource encryption
- Support for AES-256 and XOR algorithms for resources
- Inno Setup installer with PATH registration

### Changed
- Remove dotnet tool support in favor of installer-based distribution

## [1.0.6] - 2025-12-31

### Fixed
- Preserve branch targets when encrypting strings

## [1.0.5] - 2025-12-31

### Fixed
- Skip compiler-generated code in string encryption

## [1.0.4] - 2025-12-31

### Changed
- Add comprehensive documentation for public library
- Add GitHub infrastructure (issue templates, PR template)

## [1.0.2] - 2025-12-31

### Fixed
- Prevent invalid IL in string encryption

## [1.0.1] - 2025-12-31

### Added
- Single-file executable troubleshooting guide

## [1.0.0] - 2025-12-31

### Added
- Configure Obfy as a .NET global tool
- Comprehensive test coverage for obfuscators and services
- Comprehensive documentation

## [0.1.0] - 2025-12-31

### Added
- Initial release of Obfy obfuscation tool
- String encryption with AES-256 and XOR algorithms
- Symbol renaming with multiple naming modes (unreadable, sequential, hash, random)
- Control flow obfuscation with switch flattening and opaque predicates
- Anti-debugging protection injection
- Metadata removal (debug info, attributes, documentation)
- CLI with System.CommandLine
- JSON configuration file support
- Obfuscation level presets (minimal, standard, aggressive, custom)
- Symbol map export for debugging
- Batch processing of multiple files
- Support for .NET assemblies (DLL/EXE)
- Roslyn-based source code processing infrastructure
