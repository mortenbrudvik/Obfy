# Obfy MSIX package

Store-ready packaging for the WPF UI and CLI. The Inno Setup installer remains the direct-download channel.

## Build

```powershell
.\build\generate-msix-assets.ps1   # once, or after changing the app icon
.\build\build-msix.ps1
```

Output: `build/msix-output/Obfy-<version>-x64.msix`

The package is self-contained `win-x64`, full-trust, and **not** obfuscated. Store certification and Microsoft Defender scan the product binary; obfuscating Obfy itself looks like packer/evasion software.

## Sideload

The `.msix` is signed with a local `CN=Obfy` test certificate so Partner Center will accept it. Windows will **not** install that file until the cert is in `LocalMachine\TrustedPeople` (admin) or Microsoft re-signs it in the Store.

For a local try-out, register the unpacked layout (Developer Mode / sideloading enabled):

```powershell
.\build\build-msix.ps1 -Install
```

That maps Start Menu **Obfy** and the `obfy` App Execution Alias at the files under `build/msix-layout`. Uninstall before deleting the layout:

```powershell
Get-AppxPackage Obfy.Obfy | Remove-AppxPackage
```

## Microsoft Store

1. Create a Partner Center developer account and reserve the name **Obfy** (or another available title).
2. Copy the Store **Package/Identity name** and **Publisher** (`CN=...`) from Partner Center.
3. Rebuild with those values:

```powershell
.\build\build-msix.ps1 -Name "<StoreIdentityName>" -Publisher "CN=<StorePublisherId>" -PublisherDisplayName "<Publisher display name>"
```

4. Upload `build/msix-output/Obfy-*.msix` on the submission Packages page. Microsoft re-signs it; a CA-trusted cert is not required for MSIX Store uploads.
5. Provide a privacy policy URL (required for Win32/Desktop Bridge), screenshots, IARC age rating, and notes for certification: this is a developer obfuscator; include a sample DLL and expected output.

Do not change `Identity Name` or `Publisher` after the first Store submission except to the Partner Center values.
