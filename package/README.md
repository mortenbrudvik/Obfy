# Obfy MSIX package

Sideload and Microsoft Store packaging for the WPF UI and CLI. The Inno Setup installer remains the direct-download channel. This is a local package build, not a Store listing.

## Build

```powershell
.\build\generate-msix-assets.ps1   # once, or after changing the app icon
.\build\build-msix.ps1
```

Output: `build/msix-output/Obfy-<major>.<minor>.<patch>.0-x64.msix`

Pack stamps `Identity Version` from `version.json` as `major.minor.patch.0`. The committed `AppxManifest.xml` Version is a snapshot that tests pin; keep it in sync when you bump `version.json`.

The package is self-contained `win-x64`, full-trust, and **not** obfuscated. Unlike the Inno installer, this payload is not self-obfuscated. Store malware scan and Defender often flag obfuscators/packers; keep Store bits clean.

## Sideload

The default `.msix` is test-signed with a cert whose Subject matches Identity Publisher (default `CN=Obfy`). `build-msix.ps1` writes `build/msix-output/Obfy-test.cer`. Windows will not install that `.msix` until the cert is in `LocalMachine\TrustedPeople` (admin):

```powershell
Import-Certificate -FilePath .\build\msix-output\Obfy-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\build\msix-output\Obfy-*.msix
```

For a local try-out without trusting the `.msix`, register the unpacked layout (Developer Mode / sideloading enabled):

```powershell
.\build\build-msix.ps1 -Install
```

That maps Start Menu **Obfy** and **Obfy CLI**, plus the `obfy` App Execution Alias, at the files under `build/msix-layout`. The CLI is listed (not `AppListEntry=none`) because Partner Center rejects headless apps without HeadlessAppBypass. Uninstall before deleting the layout:

```powershell
Get-AppxPackage -Name Obfy.Obfy | Remove-AppxPackage
```

Use `-Name` matching the identity you packed if you overrode the default.

## Microsoft Store

Reserved name **Obfy**. Identity lives in `package/store-identity.json` (do not put these values in the committed `AppxManifest.xml` — tests pin the sideload identity).

| Field | Value |
|-------|--------|
| Identity Name | `MortenBrudvik.Obfy` |
| Publisher | `CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD` |
| Publisher display | Morten Brudvik |
| Product ID | `9NNLPK835QM4` |

Rebuild with Partner Center identity. Signing is optional — Microsoft re-signs Store uploads:

```powershell
.\build\build-msix.ps1 -Store
```

Equivalent explicit flags:

```powershell
.\build\build-msix.ps1 -Name "MortenBrudvik.Obfy" -Publisher "CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD" -PublisherDisplayName "Morten Brudvik" -SkipSign
```

Upload `build/msix-output/Obfy-*.msix` on the submission Packages page. A CA-trusted cert is not required for MSIX Store uploads.

Privacy policy URL: https://github.com/mortenbrudvik/Obfy/blob/main/PRIVACY.md  
Gist mirror (same text): https://gist.github.com/mortenbrudvik/2c9647e32d8ac32a7f1bda844923bb9a

`runFullTrust` is a restricted capability — the first Store submission needs a justification (full-trust Win32 WPF + CLI). Paste-ready notes are in [Certification](#certification).

Do not change `Identity Name` or `Publisher` after the first Store submission except to the Partner Center values. Default sideload identity (`Obfy.Obfy` / `CN=Obfy`) is not uploadable as a Store package.

## Certification

**Restricted capability (`runFullTrust`):** Full-trust Win32 WPF desktop app plus CLI. Needs unrestricted file I/O to read and write user-selected assemblies, and an App Execution Alias (`obfy.exe`). Not a sandbox UWP app.

**Notes for certification:**

Obfy is a developer obfuscation tool. It rewrites .NET assemblies the tester selects; it is not malware, a packer dropper, or an anti-virus product.

How to test:

1. Launch Obfy from Start.
2. Add `examples/BasicConsoleApp` output (build that project, or any small .NET DLL) and set an empty output folder.
3. Click Obfuscate. Expect a success log and an obfuscated DLL in the output folder.
4. Optional: open a terminal and run `obfy --help` (App Execution Alias).

Expected: the UI stays responsive, output is a loadable .NET assembly, no network calls, no elevation prompt.
