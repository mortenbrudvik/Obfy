# Changelog

All notable changes to Obfy will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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
