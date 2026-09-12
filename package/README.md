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

That maps Start Menu **Obfy** and the `obfy` App Execution Alias at the files under `build/msix-layout`. Uninstall before deleting the layout:

```powershell
Get-AppxPackage -Name Obfy.Obfy | Remove-AppxPackage
```

Use `-Name` matching the identity you packed if you overrode the default.

## Microsoft Store

1. Create a Partner Center developer account and reserve the name **Obfy** (or another available title).
2. Copy the Store **Package/Identity name** and **Publisher** (`CN=...`) from Partner Center.
3. Rebuild with those values (signing is optional — Microsoft re-signs):

```powershell
.\build\build-msix.ps1 -Name "<StoreIdentityName>" -Publisher "CN=<StorePublisherId>" -PublisherDisplayName "<Publisher display name>" -SkipSign
```

4. Upload `build/msix-output/Obfy-*.msix` on the submission Packages page. A CA-trusted cert is not required for MSIX Store uploads.
5. Provide a privacy policy URL (required for Win32/Desktop Bridge), screenshots, IARC age rating, and notes for certification: this is a developer obfuscator; include a sample DLL and expected output.
6. `runFullTrust` is a restricted capability — the first Store submission needs a justification (full-trust Win32 WPF + CLI).

Do not change `Identity Name` or `Publisher` after the first Store submission except to the Partner Center values. Default sideload identity (`Obfy.Obfy` / `CN=Obfy`) is not uploadable as a Store package.
