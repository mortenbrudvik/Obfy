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
8. Check that the `Obfy` .NET tool packs (the `publish.yml` workflow pushes it to nuget.org after the GitHub Release is published)
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

8. Check that the .NET tool packs. Do not push it from this machine: the `Obfy` package reaches nuget.org only through `.github/workflows/publish.yml`, which pushes with nuget.org Trusted Publishing (OIDC). The repository has no API key secret.

   To release, publish the GitHub Release from the tag that step 10 pushes, or start the "Publish to NuGet" workflow by hand from `main` or a tag and type the version. The push happens there, after step 10, not here. The tag (or the version you type in) must carry the same `major.minor.patch` as `version.json`, with an optional prerelease suffix. The workflow builds and packs with `-p:Version=` set to that tag or input without a leading `v`, which is how a prerelease suffix gets in; for a stable release it is the version in `version.json`. In order, the workflow:
   - runs `build/Resolve-PackageVersion.ps1` right after checkout and fails before it builds anything when the tag does not match `version.json`
   - rebuilds the native host, builds `Obfy.sln` in Release and runs the step 5 tests (a failing test stops it before the push)
   - packs `Src/Obfy.Console/Obfy.Console.csproj`
   - logs in with `NuGet/login`, which mints a short-lived key for user `mortenbrudvik`
   - asks nuget.org for that version and fails if it is already on NuGet
   - pushes the `.nupkg`

   The Trusted Publishing policy is: owner `mortenbrudvik`, repo `Obfy`, workflow `publish.yml`, environment empty. The environment is empty, so any run of the workflow from `main` or a tag can push. A manual run is a real publish (there is no dry-run input), and from any other branch the job is skipped.

   Locally, the pack is only a check that the package builds and installs. It is not the release step. The package version is the one in `version.json`; pass `-p:Version=<version>-<suffix>` only for a prerelease. The pack embeds the `Src/Obfy.NativeHost/dist/Obfy.NativeHost.exe` that is checked in, while the workflow rebuilds it first. With the nupkg in `./artifacts` you can check that its file name carries the `version.json` version and that `dotnet tool install --global Obfy --add-source ./artifacts` installs it (a prerelease also needs `--version <version>`), then run `obfy --version`.
```bash
dotnet pack Src/Obfy.Console/Obfy.Console.csproj -c Release -o ./artifacts
```

   Emergency only, when the workflow cannot run (not a release step): push from this machine. On nuget.org create a one-off API key scoped to the `Obfy` package with a short expiry, push, then delete the key. Never commit the key. The workflow refuses a version that is already on NuGet, so do not also run it for that version.
```bash
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
- .NET tool nupkg from the local pack in step 8: `artifacts/Obfy.X.Y.Z.nupkg` (or the `-o` folder you pass to `dotnet pack`). The package on nuget.org is the one the workflow packs on its runner
- Installer: `build/output/ObfySetup-X.Y.Z.exe`
- MSIX: `build/msix-output/Obfy-X.Y.Z.0-x64.msix`
- OneDrive: `~/OneDrive/Apps/Obfy/`
  - `ObfySetup-X.Y.Z.exe` - installer
  - `ObfySetup-X.Y.Z.sha256` - checksum
  - `latest.json` - version manifest
