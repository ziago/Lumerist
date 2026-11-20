; Lumerist installer for Windows (Inno Setup 6).
;
; Built by Scripts/build-windows-installer.ps1, which publishes the app first:
;   ISCC.exe /DVersion=0.9.0 /DArch=x64 /DSourceDir=<publish folder> /DOutputDir=<folder> Lumerist.iss
;
; A per-user install: no administrator rights, in %LOCALAPPDATA%\Programs\Lumerist.
; It also installs or updates the Lightroom plug-in (asking to close Lightroom
; Classic when it is open), so the user only has to open Lightroom.
;
; A newer installer run over an older version replaces the whole program
; folder (no stale files) and never touches the user's keymap, settings and
; plug-in backups, which live in %APPDATA%\Lumerist. Uninstalling removes the
; app and its "open at login" entry, and keeps that folder and the Lightroom
; plug-in, as removing the app does on the Mac.

#ifndef Version
  #error Pass the version: /DVersion=x.y.z
#endif
#ifndef SourceDir
  #error Pass the published app: /DSourceDir=<folder with Lumerist.exe>
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Never change the AppId: Windows uses it to recognise upgrades.
AppId={{1F277E78-9863-45D4-8EED-31E8CEF3D8F1}
AppName=Lumerist
AppVersion={#Version}
AppVerName=Lumerist {#Version}
AppPublisher=Lumerist
AppPublisherURL=https://github.com/ziago/Lumerist
AppSupportURL=https://github.com/ziago/Lumerist/issues
AppUpdatesURL=https://github.com/ziago/Lumerist/releases
VersionInfoVersion={#Version}
DefaultDirName={localappdata}\Programs\Lumerist
; The plug-in cannot open Lumerist from a path with ", % or ^, so the folder is fixed.
DisableDirPage=yes
DefaultGroupName=Lumerist
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Arch == "arm64"
ArchitecturesAllowed=arm64
#else
ArchitecturesAllowed=x64compatible
#endif
ArchitecturesInstallIn64BitMode={#Arch == "arm64" ? "arm64" : "x64compatible"}
MinVersion=10.0.17763
; A running Lumerist is closed before its files are replaced.
AppMutex=Local\io.github.ziago.lumerist
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=Lumerist-{#Version}-windows-{#Arch}-setup
SetupIconFile=..\Lumerist.App\Assets\Lumerist.ico
UninstallDisplayIcon={app}\Lumerist.exe
UninstallDisplayName=Lumerist
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
; An upgrade starts from an empty program folder. User data is never here.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Lumerist"; Filename: "{app}\Lumerist.exe"; Parameters: "--show-window"

[Run]
Filename: "{app}\Lumerist.exe"; Parameters: "--show-window"; Description: "Open Lumerist"; Flags: nowait postinstall skipifsilent

[Registry]
; Written by General → "Open at login"; only removed here.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Lumerist"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  { Lumerist.exe --install-plugin exit codes (PluginInstaller.CommandLineOutcome). }
  PluginDone = 0;
  LightroomIsOpen = 2;

function InstallPlugin(): Integer;
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\Lumerist.exe'), '--install-plugin', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Result := ResultCode;
end;

{ After the files are in place: the plug-in, so the user only has to open Lightroom. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;
  repeat
    Code := InstallPlugin();
    if (Code <> LightroomIsOpen) or WizardSilent then
      Break;
  until MsgBox('Lightroom Classic is open. Close it, then click Retry to install the Lumerist plug-in.' + #13#10#13#10 +
               'Click Cancel to skip it and install it later from Lumerist, with Lightroom Classic closed.',
               mbInformation, MB_RETRYCANCEL) <> IDRETRY;
  if (Code <> PluginDone) and (Code <> LightroomIsOpen) and not WizardSilent then
    MsgBox('The Lightroom plug-in could not be installed now. Lumerist shows why and installs it from its first-run guide.',
           mbInformation, MB_OK);
end;
