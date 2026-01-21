; Obfy Installer Script for Inno Setup
; Download Inno Setup from: https://jrsoftware.org/isinfo.php

#define MyAppName "Obfy"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Obfy"
#define MyAppURL "https://github.com/mortenbrudvik/Obfy"
#define MyAppExeName "obfy.exe"
#define MyAppUIExeName "ObfyUI.exe"

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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; CLI files from publish directory
Source: ".\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; UI files from publish-ui directory
Source: ".\publish-ui\*"; DestDir: "{app}\UI"; Flags: ignoreversion recursesubdirs createallsubdirs

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
Filename: "taskkill"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillObfy"
Filename: "taskkill"; Parameters: "/f /im {#MyAppUIExeName}"; Flags: runhidden; RunOnceId: "KillObfyUI"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

procedure EnvAddPath(Path: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Paths := '';

  // Check if path already exists
  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;

  // Append the new path
  if Paths <> '' then
    Paths := Paths + ';' + Path
  else
    Paths := Path;

  RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
end;

procedure EnvRemovePath(Path: string);
var
  Paths: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    exit;

  // Remove the path (with semicolon handling)
  P := Pos(';' + Uppercase(Path), Uppercase(Paths));
  if P > 0 then
  begin
    Delete(Paths, P, Length(Path) + 1);
    RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
    exit;
  end;

  P := Pos(Uppercase(Path) + ';', Uppercase(Paths));
  if P > 0 then
  begin
    Delete(Paths, P, Length(Path) + 1);
    RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
    exit;
  end;

  P := Pos(Uppercase(Path), Uppercase(Paths));
  if P > 0 then
  begin
    Delete(Paths, P, Length(Path));
    RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);
  end;
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

// Kill running instances before installation
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Try to kill any running instances
  Exec('taskkill', '/f /im obfy.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill', '/f /im ObfyUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
