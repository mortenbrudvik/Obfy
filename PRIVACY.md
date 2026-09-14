# Privacy Policy for Obfy

**App name:** Obfy  
**Publisher:** Morten Brudvik  
**Microsoft Store product ID:** 9NNLPK835QM4  
**Last updated:** 14 September 2026

This privacy policy applies to the **Obfy** desktop application distributed through the Microsoft Store (WPF UI and the bundled `obfy` command-line tool). It describes what information Obfy processes and how that information is handled.

Obfy is a local .NET obfuscation tool. It does **not** create accounts, does **not** include advertising, and does **not** send telemetry, analytics, crash reports, or usage data to Morten Brudvik or to any third party.

## Personal information

Obfy does **not** collect, transmit, or sell personal information.

The app does not require a login. It does not access your Microsoft account, contacts, calendar, location, camera, microphone, or other device identifiers for identification or tracking.

When you choose files or folders to obfuscate, Obfy reads those files on your computer and writes output to the location you specify. File names and paths may appear in local log files so you can diagnose a failed run. That data stays on your device unless you copy it elsewhere.

## Data stored on your device

Obfy stores the following **locally** so the app can remember your preferences and help you debug problems:

| Data | Typical location | Purpose |
|------|------------------|---------|
| UI preferences (last output folder, symbol-map option) | `%APPDATA%\Obfy\ui-preferences.json` | Remember desktop-app choices |
| Settings you save (`obfy.json`) | A path you choose (`obfy config generate` / Save Configuration default to the current folder) | Persist obfuscation configuration |
| Diagnostic logs | `%LOCALAPPDATA%\Obfy\Logs\` | Record errors and run details (may include file paths) |
| Optional incremental cache | Next to an output you generate (`*.obfycache`) | Skip unchanged work on later runs |
| Output you request | Folders you choose | Obfuscated assemblies, optional symbol maps, HTML reports |

Logs rotate daily and keep a limited archive (about 30 days). You can delete any of these files at any time; Obfy will recreate settings and logs as needed.

## Network

The Store-distributed Obfy app does not contact Morten Brudvik’s servers. It does not check for updates itself; Microsoft Store handles download and update of the Store package.

If you later use a non-Store distribution (for example the NuGet global tool or a direct-download installer), those builds likewise perform obfuscation locally and do not phone home.

Microsoft may collect information about Store acquisition, licensing, and updates under [Microsoft’s privacy statement](https://privacy.microsoft.com/). That collection is Microsoft’s, not Obfy’s.

## Full-trust access

Obfy is a classic Windows desktop (Win32) app. The package declares the restricted `runFullTrust` capability so it can:

- Read assemblies, projects, and solutions you select
- Write obfuscated output, reports, and symbol maps where you ask
- Register the `obfy` App Execution Alias for command-line use

Obfy does not scan your disk for assemblies unless you add those files. It does not upload the files you obfuscate.

## Children

Obfy is a developer tool. It is not directed at children and does not knowingly collect information from children.

## Changes

If this policy changes, the updated text will be published at this URL with a new “Last updated” date.

## Contact

Questions about this policy or about Obfy:

- Issues: [https://github.com/mortenbrudvik/Obfy/issues](https://github.com/mortenbrudvik/Obfy/issues)
- Security reports: [https://github.com/mortenbrudvik/Obfy/security/advisories/new](https://github.com/mortenbrudvik/Obfy/security/advisories/new)
