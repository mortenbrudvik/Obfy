; Obfy Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isinfo.php

; The version is not written here. version.json is the only source: build-installer.ps1 reads it and passes it
; as /DMyAppVersion=major.minor.patch. There is deliberately no default: a default would be a second copy to
; bump, and an installer built from the Inno Setup IDE would silently carry the wrong version.
#ifndef MyAppVersion
  #error MyAppVersion is not defined. Build the installer with build\build-installer.ps1, which reads version.json and passes /DMyAppVersion=major.minor.patch to ISCC.
#endif

#define MyAppName "Obfy"
#define MyAppPublisher "Obfy"
#define MyAppURL "https://github.com/mortenbrudvik/Obfy"
#define MyAppExeName "obfy.exe"
#define MyAppUIExeName "ObfyUI.exe"
; ObfyUI.exe hosts WebView2 and loads its page from {app}\UI\Assets, so the Evergreen runtime must be present.
#define WebView2ClientGuid "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
; Unique application ID - generated GUID for Obfy
AppId={{8F4A9B2C-3D5E-4F6A-8B7C-9D0E1F2A3B4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output settings
OutputDir=.\output
OutputBaseFilename=ObfySetup-{#MyAppVersion}
; Use LZMA2 compression for best compression ratio
Compression=lzma2/ultra64
SolidCompression=yes
; Require Windows 10 or later
MinVersion=10.0
; 64-bit only
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Modern installer style
WizardStyle=modern
DisableDirPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
; Require admin privileges for PATH modification
PrivilegesRequired=admin
; Tell running programs (Explorer, new shells) that the system Path changed at install and uninstall
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The dontcopy prerequisite MUST be listed first. With SolidCompression,
; ExtractTemporaryFile has to decompress everything ahead of it in the block, so
; listing it after the publish payload would stall setup on exactly the machines
; that lack WebView2. build-installer.ps1 downloads it into build\redist.
Source: ".\redist\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy noencryption
; CLI files from publish directory (no symbols: build-installer.ps1 self-obfuscates the DLLs, but the publish
; folder still holds their original PDBs)
Source: ".\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; UI files from publish-ui directory (includes the web Assets\ folder; no symbols or source maps)
Source: ".\publish-ui\*"; DestDir: "{app}\UI"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.map"

[Icons]
; Start Menu shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\UI\{#MyAppUIExeName}"; WorkingDir: "{app}\UI"
Name: "{group}\{#MyAppName} Command Prompt"; Filename: "{cmd}"; Parameters: "/k ""{app}\{#MyAppExeName}"" --help"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop shortcut
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\UI\{#MyAppUIExeName}"; WorkingDir: "{app}\UI"

[Run]
; Launch UI after installation
Filename: "{app}\UI\{#MyAppUIExeName}"; Description: "Launch Obfy"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill running instances before uninstall
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillObfy"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppUIExeName}"; Flags: runhidden; RunOnceId: "KillObfyUI"

[Code]
const
  WebView2ExitRebootRequired = 3010;
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

// The Evergreen runtime registers its version ('pv') under the EdgeUpdate client GUID,
// machine-wide or per-user, in either registry view. Empty and 0.0.0.0 mean "not installed".
function WebView2VersionIsValid(Version: string): Boolean;
begin
  Result := (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2RuntimeInstalled: Boolean;
var
  Version: string;
begin
  Result := True;

  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{#WebView2ClientGuid}', 'pv', Version) and WebView2VersionIsValid(Version) then
    Exit;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2ClientGuid}', 'pv', Version) and WebView2VersionIsValid(Version) then
    Exit;
  if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{#WebView2ClientGuid}', 'pv', Version) and WebView2VersionIsValid(Version) then
    Exit;
  if RegQueryStringValue(HKCU, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2ClientGuid}', 'pv', Version) and WebView2VersionIsValid(Version) then
    Exit;

  Result := False;
end;

// Runs the bundled Evergreen bootstrapper silently. Returns '' on success, otherwise the
// message that aborts setup.
function InstallWebView2Runtime(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Bootstrapper: string;
begin
  Result := '';

  try
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  except
    Result := 'The WebView2 bootstrapper is missing from the installer payload.' + #13#10 +
      'Re-run build\build-installer.ps1 to download build\redist\MicrosoftEdgeWebview2Setup.exe.';
    Exit;
  end;

  Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  if not FileExists(Bootstrapper) then
  begin
    Result := 'The WebView2 bootstrapper could not be extracted to the temporary folder.';
    Exit;
  end;

  if not Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'Failed to start the WebView2 Runtime installer.';
    Exit;
  end;

  // 3010 = installed successfully, reboot required. Reported through NeedsRestart rather
  // than a MsgBox so it does not block an unattended /SILENT install.
  if ResultCode = WebView2ExitRebootRequired then
  begin
    NeedsRestart := True;
    Exit;
  end;

  if ResultCode <> 0 then
    Result := 'WebView2 Runtime installation failed (exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
      'Install WebView2 manually from https://developer.microsoft.com/microsoft-edge/webview2/ and re-run setup.';
end;

// Terminates running instances after the user confirms installation, using the qualified System32 executable.
procedure StopRunningInstances;
var
  TaskKillPath: string;
  ResultCode: Integer;
begin
  TaskKillPath := ExpandConstant('{sys}\taskkill.exe');
  Exec(TaskKillPath, '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(TaskKillPath, '/f /im {#MyAppUIExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Runs after the user confirms the install, so running instances are only stopped and the
// machine-wide runtime is only installed once they have committed. A non-empty result aborts setup with that message.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopRunningInstances;
  if not IsWebView2RuntimeInstalled then
    Result := InstallWebView2Runtime(NeedsRestart);
end;

procedure EnvAddPath(Path: string);
var
  Paths: string;
begin
  if RegValueExists(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path') then
  begin
    // Do not overwrite an existing Path if querying it fails (e.g. unexpected type or length).
    if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
      exit;
  end
  else
    Paths := '';

  // Check if path already exists
  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;

  // Append the new path
  if Paths <> '' then
    Paths := Paths + ';' + Path
  else
    Paths := Path;

  // The system Path is a REG_EXPAND_SZ. Writing it back as REG_SZ would stop %SystemRoot% and
  // friends from expanding for every program on the machine.
  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
end;

// Rebuilds Paths entry by entry and drops only entries equal to Path (case-insensitive).
// A substring or prefix match would also hit a sibling such as "...\Obfy-Beta" listed earlier.
// Every other entry, including empty ones, is kept exactly as it was.
function PathWithoutEntry(Paths, Path: string; var Removed: Boolean): string;
var
  Remaining, Entry: string;
  P: Integer;
  First: Boolean;
begin
  Result := '';
  Remaining := Paths;
  Removed := False;
  First := True;
  repeat
    P := Pos(';', Remaining);
    if P > 0 then
    begin
      Entry := Copy(Remaining, 1, P - 1);
      Remaining := Copy(Remaining, P + 1, Length(Remaining) - P);
    end
    else
    begin
      Entry := Remaining;
      Remaining := '';
    end;

    if CompareText(Entry, Path) = 0 then
      Removed := True
    else
    begin
      if not First then
        Result := Result + ';';
      Result := Result + Entry;
      First := False;
    end;
  until P = 0;
end;

procedure EnvRemovePath(Path: string);
var
  Paths: string;
  Removed: Boolean;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    exit;

  Paths := PathWithoutEntry(Paths, Path, Removed);
  if Removed then
    RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnvAddPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    EnvRemovePath(ExpandConstant('{app}'));
end;
