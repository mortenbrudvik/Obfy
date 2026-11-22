# Publish Command

Build, package, and distribute Obfy for release.

## Workflow

1. Determine version bump (major/minor/patch)
2. Update the version in `version.json` and the MSIX manifest snapshot
3. Update CHANGELOG.md with release date
4. Build release configuration
5. Run all tests
6. Build installer
7. Build MSIX
8. Pack and push the `Obfy` .NET tool to nuget.org
9. Copy installer to OneDrive
10. Create git tag
11. Push tag to remote

## Arguments

- `[major|minor|patch]`: Version bump type (default: analyze commits)

## Instructions

1. Analyze commits since last tag to determine version bump:
   - `feat:` = minor
   - `fix:` = patch
   - `BREAKING CHANGE:` = major

2. Update the version in these two files and nowhere else:
   - `version.json`: the `major`, `minor` and `patch` integers. This is the only place the product version is written. `Directory.Build.props` gives it to every project under `Src` (assembly, file and package version of the CLI, Core and the desktop app), `build-installer.ps1` passes it to Inno Setup, `build-msix.ps1` stamps the MSIX identity from it, and `publish.yml` checks the release tag against it. Do not edit a `<Version>` in a project file or `build/ObfySetup.iss` (it has no version of its own, and a test fails on a hard-coded one)
   - `package/AppxManifest.xml`: the `Identity Version` snapshot, `major.minor.patch.0`. It is the one copy kept by hand; a test fails until it matches `version.json` (`build-msix.ps1` re-stamps the packed manifest from `version.json` anyway)

   Not bumped here: the Visual Studio extension has its own version in `Src/Obfy.VisualStudio/source.extension.vsixmanifest`. `version.json` must be exactly the three integer keys (0 to 65535, no leading zeros, no other keys); anything else fails the build with a message that names the file.

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

6. Build installer (needs Inno Setup 6, the .NET SDK, Node 24, an interactive Windows session and the WebView2 Runtime). The script protects Obfy's own assemblies with `build/self-obfuscation.json`, then starts the protected `obfy.exe` and the protected `ObfyUI.exe` and stops with an error, building no installer, if either does not start or does not complete a run. Fix that failure before going on; see "Version and installer" in `CLAUDE.md`. Do not use `-SkipObfuscation` or `-SkipPublish` for a release: they skip the protection and these checks
```powershell
.\build\build-installer.ps1
```

7. Build MSIX (not obfuscated; Store certification scans the product binary).
   For a Store upload, pass Partner Center `-Name` / `-Publisher` and `-SkipSign`.
   Do not copy the `.msix` to OneDrive — upload it in Partner Center separately.
```powershell
.\build\build-msix.ps1
```

8. Pack and push the .NET tool (or rely on `.github/workflows/publish.yml` when the GitHub Release is published). The package version is the one in `version.json`; pass `-p:Version=<version>-<suffix>` only for a prerelease.
   `publish.yml` runs `build/Resolve-PackageVersion.ps1` first, right after checkout: the release tag (or the version typed into a manual run) must have the same `major.minor.patch` as `version.json`, with an optional prerelease suffix, or the workflow fails before it builds anything. Create the release from a tag that matches (`v<version>`, step 10).
   nuget.org Trusted Publishing (OIDC) — no API key secret. Policy: owner `mortenbrudvik`, repo `Obfy`, workflow `publish.yml`, environment empty.
```bash
dotnet pack Src/Obfy.Console/Obfy.Console.csproj -c Release -o ./artifacts
dotnet nuget push ./artifacts/*.nupkg --api-key %NUGET_API_KEY% --source https://api.nuget.org/v3/index.json --skip-duplicate
```

9. Copy the Inno installer to OneDrive:
```powershell
.\build\copy-to-onedrive.ps1 -Force
```

10. If all steps pass, commit and tag:
```bash
git add -A
git commit -m "chore: release v<version>"
git tag v<version>
git push && git push --tags
```

## Output Locations

- Release binaries: `Src/Obfy.Console/bin/Release/net10.0/`
- .NET tool nupkg: `artifacts/Obfy.X.Y.Z.nupkg` (or the `-o` folder you pass to `dotnet pack`)
- Installer: `build/output/ObfySetup-X.Y.Z.exe`
- MSIX: `build/msix-output/Obfy-X.Y.Z.0-x64.msix`
- OneDrive: `~/OneDrive/Apps/Obfy/`
  - `ObfySetup-X.Y.Z.exe` - installer
  - `ObfySetup-X.Y.Z.sha256` - checksum
  - `latest.json` - version manifest
