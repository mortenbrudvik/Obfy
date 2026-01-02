# Changelog

All notable changes to Obfy will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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
