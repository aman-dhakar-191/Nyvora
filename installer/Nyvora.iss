; Inno Setup script for Nyvora. Per-user install (no admin): program files in %LOCALAPPDATA%\Programs\Nyvora,
; user data stays in %APPDATA%\Nyvora (Roaming) and is never touched by install or uninstall.
; Build:  set NYVORA_VERSION=1.2.3, then ISCC.exe /DSourceDir=<published folder> /O<output folder> installer\Nyvora.iss
; Self-updates replace files in {app} in place, so the version shown in Windows' installed-apps list is the
; version that was installed, not the version after self-updates.

; The version arrives as an environment variable, i.e. as text. (A command-line /DAppVersion=1.2.0 is parsed by the
; preprocessor as an expression and comes out garbled.)
#define AppVersion GetEnv("NYVORA_VERSION")
#if AppVersion == ""
  #undef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\Nyvora-win-x64"
#endif

[Setup]
AppId={{8E3C1A52-6B1D-4F57-9C0E-2A7D5B94F3C8}
AppName=Nyvora
AppVersion={#AppVersion}
AppVerName=Nyvora {#AppVersion}
AppPublisher=Nyvora
DefaultDirName={localappdata}\Programs\Nyvora
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
OutputBaseFilename=Nyvora-Setup-v{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Nyvora
UninstallDisplayIcon={app}\Nyvora.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Nyvora"; Filename: "{app}\Nyvora.exe"
Name: "{userdesktop}\Nyvora"; Filename: "{app}\Nyvora.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Nyvora.exe"; Description: "Start Nyvora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The app registers itself to start at sign-in; take that entry out and stop a running copy so files can be removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/im Nyvora.exe /f"; Flags: runhidden; RunOnceId: "StopNyvora"
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Nyvora /f"; Flags: runhidden; RunOnceId: "RemoveSignInEntry"

[UninstallDelete]
; Files added by self-updates are not in the installer's list, so remove the whole program folder.
Type: filesandordirs; Name: "{app}"
Type: files; Name: "{userdesktop}\Nyvora.lnk"
Type: files; Name: "{userprograms}\Nyvora.lnk"
