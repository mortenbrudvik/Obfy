# Publish Command

Build, package, and distribute Obfy for release.

## Workflow

1. Determine version bump (major/minor/patch)
2. Update the version in `version.json` and the MSIX manifest snapshot
3. Update CHANGELOG.md with release date
4. Build release configuration
5. Run the release tests (the `publish.yml` filter, which leaves out UI and platform tests)
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

   Not bumped here: the Visual Studio extension has its own version in `Src/Obfy.VisualStudio/source.extension.vsixmanifest`. `Obfy.PackedBootstrap` and `Obfy.VmRuntime` are not versioned from `version.json` either (`Directory.Build.props` skips all three). `version.json` must be exactly the three integer keys (0 to 65535, no leading zeros, no other keys); anything else fails the build with a message that names the file.

3. Update CHANGELOG.md:
   - Move Unreleased to new version section
   - Add release date

4. Build release:
```bash
dotnet build Obfy.sln -c Release
```

5. Run the tests `publish.yml` runs. The filter leaves out `Category=UI` and `Category=Platform`; those need an interactive session and extra SDKs (`docs/Testing.md`). The workflow does not run the web tests either.
```bash
dotnet test Obfy.sln -c Release --filter "Category!=UI&Category!=Platform"
```

6. Build the installer (Inno Setup 6, the .NET SDK, Node 24, an interactive Windows session and the WebView2 Runtime). Step 5 of `build/build-installer.ps1` protects each publish folder in one closed-set run (`build/publish` for the CLI, `build/publish-ui` for the app) with `build/self-obfuscation.json` and no technique flags. The assemblies call each other, so they are not protected one at a time, and nothing is copied back until every output of that run exists. It then checks the protected programs and throws, building no installer, if a check fails. The protected files stay in those publish folders.

   The CLI check is `obfy --version` (the output must contain a version), a throw-away net10.0 console protected at `-l standard` (the output must report encrypted strings, and the program must run), `config generate` (the file must contain `"level"` and `"stringEncryption"`), and a handwritten configuration with every protection off (no "Unknown configuration key", the run reports "No transformations were applied", and the output runs). The desktop check starts `ObfyUI.exe` and, over the DevTools protocol, requires the Files screen, the bridge (`app.info`, `files.get`, `settings.applyLevel`), and one `run.start` that finishes with no failures within 120 seconds, then runs the assembly that run wrote. The assembly list, the exclusions, and how a failure reads are under "Version and installer" in `CLAUDE.md`. Do not use `-SkipObfuscation` or `-SkipPublish` for a release: they skip the protection and these checks.
```powershell
.\build\build-installer.ps1
```

7. Build the MSIX (not obfuscated; Store certification scans the product binary).
   The default is a sideload package. For a Store upload, pass `-Store`: it reads `package/store-identity.json` and does not sign, because Microsoft re-signs the upload. Passing `-Name`, `-Publisher`, `-PublisherDisplayName` and `-SkipSign` is that same identity written out by hand.
   Do not copy the `.msix` to OneDrive. Upload it in Partner Center separately.
```powershell
.\build\build-msix.ps1
.\build\build-msix.ps1 -Store
```

8. Pack the .NET tool. The package version is the one in `version.json`. Pass `-p:Version=<version>-<suffix>` only for a prerelease. Publishing the GitHub Release (from a tag that matches, step 10) runs `.github/workflows/publish.yml`, which is the path that pushes to nuget.org. A manual run of that workflow is allowed from `main` or a tag, and the version you type in must carry the same `major.minor.patch` as `version.json`, with an optional prerelease suffix. The workflow runs `build/Resolve-PackageVersion.ps1` right after checkout and fails before it builds anything when the tag does not match. It then rebuilds the native host, packs, and pushes with nuget.org Trusted Publishing (OIDC): `NuGet/login` mints a short-lived key for user `mortenbrudvik`. Policy: owner `mortenbrudvik`, repo `Obfy`, workflow `publish.yml`, environment empty. The repository has no API key secret. The workflow refuses a version that is already on NuGet. A push from this machine needs an API key you create on nuget.org for that push; do not commit it.
```bash
dotnet pack Src/Obfy.Console/Obfy.Console.csproj -c Release -o ./artifacts
dotnet nuget push ./artifacts/*.nupkg --api-key <key-from-nuget.org> --source https://api.nuget.org/v3/index.json
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
