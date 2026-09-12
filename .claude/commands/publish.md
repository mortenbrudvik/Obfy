# Publish Command

Build, package, and distribute Obfy for release.

## Workflow

1. Determine version bump (major/minor/patch)
2. Update version in project files and installer script
3. Update CHANGELOG.md with release date
4. Build release configuration
5. Run all tests
6. Build installer
7. Build MSIX
8. Copy installer to OneDrive
9. Create git tag
10. Push tag to remote

## Arguments

- `[major|minor|patch]`: Version bump type (default: analyze commits)

## Instructions

1. Analyze commits since last tag to determine version bump:
   - `feat:` = minor
   - `fix:` = patch
   - `BREAKING CHANGE:` = major

2. Update version in:
   - `version.json` (source of truth for installer and MSIX pack)
   - `Src/Obfy.Console/Obfy.Console.csproj`
   - `Src/Obfy.Core/Obfy.Core.csproj`
   - `build/ObfySetup.iss` (AppVersion line)
   - `package/AppxManifest.xml` (Identity Version snapshot, `major.minor.patch.0` — tests pin this to `version.json`; `build-msix.ps1` re-stamps the packed manifest from `version.json`)

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

7. Build MSIX (not obfuscated; Store certification scans the product binary).
   For a Store upload, pass Partner Center `-Name` / `-Publisher` and `-SkipSign`.
   Do not copy the `.msix` to OneDrive — upload it in Partner Center separately.
```powershell
.\build\build-msix.ps1
```

8. Copy the Inno installer to OneDrive:
```powershell
.\build\copy-to-onedrive.ps1 -Force
```

9. If all steps pass, commit and tag:
```bash
git add -A
git commit -m "chore: release v<version>"
git tag v<version>
git push && git push --tags
```

## Output Locations

- Release binaries: `Src/Obfy.Console/bin/Release/net10.0-windows/`
- Installer: `build/output/ObfySetup-X.Y.Z.exe`
- MSIX: `build/msix-output/Obfy-X.Y.Z.0-x64.msix`
- OneDrive: `~/OneDrive/Apps/Obfy/`
  - `ObfySetup-X.Y.Z.exe` - installer
  - `ObfySetup-X.Y.Z.sha256` - checksum
  - `latest.json` - version manifest
