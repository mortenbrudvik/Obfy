# Privacy Policy for Obfy

**App name:** Obfy  
**Publisher:** Morten Brudvik  
**Microsoft Store product ID:** 9NNLPK835QM4  
**Last updated:** 21 November 2026  
**Web version:** [https://brudvik.com/obfy/privacy](https://brudvik.com/obfy/privacy)

This privacy policy applies to the **Obfy** desktop application distributed through the Microsoft Store (a native WPF shell that shows its window with Microsoft Edge WebView2, and the bundled `obfy` command-line tool). It also covers the command-line tool from NuGet, the installer on GitHub, and the Visual Studio 2022 extension. It describes what information Obfy processes and how that information is handled, and it describes version 2.0.0.

Obfy is a local .NET obfuscation tool. It does **not** create accounts, does **not** include advertising, and does **not** send telemetry, analytics, crash reports, or usage data to Morten Brudvik or to any third party. It makes no network requests of its own, and it has no update check and no license check.

## Personal information

Obfy does **not** collect, transmit, or sell personal information.

The app does not require a login. It does not access your Microsoft account, contacts, calendar, location, camera, microphone, or other device identifiers for identification or tracking. It adds no telemetry and no network calls to the assemblies it writes.

Obfy reads only what you give it: the files, projects, and solutions you add (with the file picker, by dropping them on the window, or on the command line), the configuration file you open, and the key file you name if you turn on signing. For a solution or a project it also reads the project files and looks for each project's output in its `bin\Release` or `bin\Debug` folder. It does not search your disk for anything else, and it never uploads a file. Obfy writes protected copies to the folder you choose or, if you choose none, next to the input (for example `name.obfuscated.dll`, or an `obfy-out` folder for a solution).

File names and paths appear in local log files so you can diagnose a failed run (see below). That data stays on your device unless you copy it elsewhere.

## Data stored on your device

Obfy stores the following **locally** so the app can remember your preferences and help you debug problems:

| Data | Typical location | Purpose |
|------|------------------|---------|
| UI preferences (last output folder, symbol-map option) | `%APPDATA%\Obfy\ui-preferences.json` | Remember desktop-app choices. An output folder given on the command line for one session is not saved |
| Settings you save (`obfy.json`) | A path you choose, or the file you opened (`obfy config generate` writes to the current folder unless you say otherwise) | Persist obfuscation configuration. It can include the path to your key file, the name of the environment variable that holds its password, and a watermark id you typed. It never holds the key or the password |
| Diagnostic logs | `%LOCALAPPDATA%\Obfy\Logs\` | Record errors and run details (see below) |
| Optional incremental cache | Next to an output you generate (`*.obfycache`) | Skip unchanged work on later runs. Off by default (`--incremental`). It holds one SHA-256 fingerprint of the input, the settings, and the Obfy version, and nothing else |
| WebView2 working files | `%LOCALAPPDATA%\Obfy\WebView2\` | Microsoft Edge WebView2's own working files for the app window |
| Temporary files | `%TEMP%` | Work files while Obfy runs, deleted afterwards (a crash can leave some behind). When a run of several files or a solution replaces files already in its output folder, a hidden `.obfyprev` backup sits beside each until the run has finished |
| Output you request | Folders you choose | Obfuscated assemblies, optional symbol maps (they list the original name of every renamed symbol, so keep them as private as your source), HTML or JSON reports, and the launcher files that go with a packed output |

**What the logs contain.** Log files record what Obfy did: when it ran, which protections ran and for how long, counts, and what went wrong. They name the files Obfy read and wrote, with their full paths, and the assemblies, types, methods, and resources it worked on, so a log can show your Windows user name and the names in your code. They do not hold the contents of your files, your string literals, or your key or password, and what the app's window sends to Obfy is logged only by name and length, never what you typed. A few messages repeat a setting you typed, such as a watermark id, and an error message can name a file or a folder. The desktop app and the command line each start a new log file every day they run. Obfy keeps the newest 31 of each and deletes older ones. Nothing in them is sent anywhere.

You can delete any of these files at any time; Obfy will recreate settings and logs as needed. To remove everything Obfy keeps, quit Obfy and delete the `Obfy` folders under `%APPDATA%` and `%LOCALAPPDATA%` and any `*.obfycache` files. Protected copies, symbol maps, reports, and configurations you saved stay where you put them. For the Microsoft Store version, Windows may keep these folders inside Obfy's own package folder under `%LOCALAPPDATA%\Packages` instead, and removes them when you uninstall Obfy.

## The app window and WebView2

The desktop app's window is drawn by Microsoft Edge WebView2, a separate Microsoft component that Windows keeps up to date. Obfy shows only its own page in it, which comes with the app and is read from the app's own folder. It blocks every other address the window might try to load or open, switches off the SmartScreen check that would send the addresses the window loads to Microsoft, and turns off password saving, form autofill, and single sign-on with your Windows account. WebView2 itself is Microsoft's, and the [Microsoft Privacy Statement](https://www.microsoft.com/privacy/privacystatement) covers it.

If the WebView2 Runtime is missing, Obfy says so, gives Microsoft's download address in the message, and closes. It downloads nothing itself.

This window is new in version 2.0.0. Version 1.3.0, which NuGet and GitHub Releases carry until 2.0.0 is released there, has a native Windows window without WebView2, and makes no network requests either.

## Settings, clipboard, and links

- Obfy reads one Windows setting, whether apps should be light or dark, so that its window matches.
- The only thing Obfy puts on your clipboard is the run log, when you press Copy in the Log tab. It never reads your clipboard.
- Obfy opens a web page only for its online guide, and only when you press one of the guide links described next. It starts File Explorer only when you ask it to open the output folder, show a file in its folder, or open the log folder.

## The online guide

The Guide screen in the desktop app has an **Open the full guide online** button, and the detail panels on the Protection screen each have a **Read more online** link. Each opens the guide on Morten Brudvik's website, `https://brudvik.com/obfy/guide` or one section of it, in your default browser, and only when you press it. Obfy makes no request to that address itself: it hands the address to Windows, which opens your browser. Obfy builds the address from a fixed list of section names, so it carries nothing from your files or your settings, and the window cannot make Obfy open any other address. These are the only web addresses Obfy opens.

Once the guide is open, it is a page on brudvik.com like any other. Like any visit to a web page, it shows your IP address to the site, and what the site receives is between your browser and the site, not Obfy.

## Command line and IDE extensions

`obfy` does the same work as the app, without a window. It reads the files and the configuration you name on its command line, writes where you tell it (`-o`, `--map`, `--report`), prints to its console, and writes the same kind of log files. `obfy config generate` writes a `$schema` line that names a file on raw.githubusercontent.com (GitHub). Obfy never fetches it, but an editor that supports JSON schemas may.

The Visual Studio 2022 extension runs the `obfy` tool installed on your PC and shows its output in the IDE. It keeps its options in the IDE's own settings and a per-project `obfy.json` in the project folder, and it makes no network requests of its own. When you run Obfuscate, it replaces the project's built output with the protected copy, and it does the same after every build if you switch on post-build obfuscation for a project (it is off until you do).

## Network

The Obfy app makes no network requests of its own and does not contact Morten Brudvik's servers. It does not check for updates itself; Microsoft Store handles download and update of the Store package.

If you later use a non-Store distribution (for example the NuGet global tool or a direct-download installer), those builds likewise perform obfuscation locally and do not phone home. `dotnet tool install --global Obfy` downloads the tool from NuGet; that download is .NET's and NuGet's, not Obfy's. The installer from GitHub asks for administrator rights because it adds Obfy's folder to the system `PATH`, and it creates shortcuts. If the WebView2 Runtime is missing, it runs Microsoft's WebView2 bootstrapper, which downloads the runtime from Microsoft. Uninstalling takes the folder off the `PATH` again.

Microsoft may collect information about Store acquisition, licensing, and updates under [Microsoft's privacy statement](https://www.microsoft.com/privacy/privacystatement). That collection is Microsoft's, not Obfy's.

## Full-trust access

Obfy is a classic Windows desktop (Win32) app. The package declares the restricted `runFullTrust` capability so it can:

- Read assemblies, projects, and solutions you select
- Write obfuscated output, reports, and symbol maps where you ask
- Register the `obfy` App Execution Alias for command-line use

It declares no other capability. Windows does not sandbox a full-trust app, which is why this policy spells out what Obfy reads and writes.

Obfy does not scan your disk for assemblies unless you add those files. It does not upload the files you obfuscate.

## Children

Obfy is a developer tool. It is not directed at children and does not knowingly collect information from children.

## Changes

If this policy changes, the updated text will be published at this URL and at the web version, with a new “Last updated” date.

## Contact

Questions about this policy or about Obfy:

- X: [@brudvik](https://x.com/brudvik)
- Issues: [https://github.com/mortenbrudvik/Obfy/issues](https://github.com/mortenbrudvik/Obfy/issues)
- Security reports: [https://github.com/mortenbrudvik/Obfy/security/advisories/new](https://github.com/mortenbrudvik/Obfy/security/advisories/new)
