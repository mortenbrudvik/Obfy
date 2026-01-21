# Publish Command

Build, package, and distribute Obfy for release.

## Workflow

1. Determine version bump (major/minor/patch)
2. Update version in project files and installer script
3. Update CHANGELOG.md with release date
4. Build release configuration
5. Run all tests
6. Build installer
7. Copy installer to OneDrive
8. Create git tag
9. Push tag to remote

## Arguments

- `[major|minor|patch]`: Version bump type (default: analyze commits)

## Instructions

1. Analyze commits since last tag to determine version bump:
   - `feat:` = minor
   - `fix:` = patch
   - `BREAKING CHANGE:` = major

2. Update version in:
   - `Src/Obfy.Console/Obfy.Console.csproj`
   - `Src/Obfy.Core/Obfy.Core.csproj`
   - `build/ObfySetup.iss` (AppVersion line)

3. Update CHANGELOG.md:
   - Move Unreleased to new version section
   - Add release date

4. Build release:
```bash
dotnet build Obfy.sln -c Release
```

5. Run tests:
```bash
dotnet test Obfy.sln -c Release
```

6. Build installer:
```powershell
.\build\build-installer.ps1
```

7. Copy to OneDrive:
```powershell
.\build\copy-to-onedrive.ps1 -Force
```

8. If all steps pass, commit and tag:
```bash
git add -A
git commit -m "chore: release v<version>"
git tag v<version>
git push && git push --tags
```

## Output Locations

- Release binaries: `Src/Obfy.Console/bin/Release/net10.0-windows/`
- Installer: `build/output/ObfySetup-X.Y.Z.exe`
- OneDrive: `~/OneDrive/Apps/Obfy/`
  - `ObfySetup-X.Y.Z.exe` - installer
  - `ObfySetup-X.Y.Z.sha256` - checksum
  - `latest.json` - version manifest
