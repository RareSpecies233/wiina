; Wiina — Inno Setup 6 script: per-user installer for the portable payload.
;
; Two distribution forms are produced from the same payload:
;   * portable: artifacts\wiina-<version>-win-x64.zip (scripts\package.ps1);
;     it ships "Uninstall Wiina.exe" next to Wiina.exe, see src\Wiina.Uninstall.
;   * installed: this script, compiled by scripts\release.ps1 into
;     artifacts\Wiina-Setup-<version>-x64.exe when ISCC is available.
; The installed copy uses Inno's own uninstaller, so the portable uninstall
; entry point is excluded from [Files] below.
;
; The install is per-user (PrivilegesRequired=lowest): the app only writes to
; %LOCALAPPDATA%\Wiina and HKCU, so installing needs no elevation and no
; machine-wide state is touched by the installer itself. Wiina's file
; associations are opt-in from the app settings; the uninstaller ALWAYS removes
; every registry entry Wiina created and only asks whether to delete
; %LOCALAPPDATA%\Wiina. The "associate as administrator" action inside the app
; may also have created HKLM copies of those keys, so the uninstaller raises a
; single UAC prompt when it starts (InitializeUninstall re-launches itself
; elevated) and then removes those copies directly: no PowerShell helper
; window, and no "uninstall was incomplete" warning at the end.
;
; To build manually:
;   1. Unpack the portable zip into artifacts\publish (or run
;      scripts\package.ps1 and extract its output).
;   2. C:\Program Files\Inno Setup 6\ISCC.exe installer\wiina.iss
;   3. Output goes to artifacts\ (override with /O<dir>).
;
; The UI is Chinese (Simplified) and English; installer\ChineseSimplified.isl is
; the official Inno Setup translation (UTF-8 with BOM). The wizard picks the
; language from the Windows UI language and keeps a language drop-down.

#define MyAppName "Wiina"
; Overridable so a test build can be produced without touching the release
; version: ISCC.exe /DMyAppVersion=1.3.7 installer\wiina.iss
#ifndef MyAppVersion
  #define MyAppVersion "1.3.7"
#endif
#define MyAppPublisher "Wiina contributors"
#define MyAppUrl "https://github.com/RareSpecies233/wiina"
#define MyAppExeName "Wiina.exe"
; Name of the portable uninstaller produced by src\Wiina.Uninstall; excluded
; from the installed copy because Inno owns the uninstaller there.
#ifndef MyUninstallerExe
  #define MyUninstallerExe "Uninstall Wiina.exe"
#endif
; Publish output produced by scripts\package.ps1 or:
;   dotnet publish src\Wiina.App\Wiina.App.csproj -c Release -r win-x64 --self-contained true ^
;     -o artifacts\publish -p:Version=<version> -p:DebugType=None -p:DebugSymbols=false
#ifndef MyPublishDir
  #define MyPublishDir "..\artifacts\publish"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts"
#endif

[Setup]
; Keep this AppId stable: it is the upgrade/uninstall identity of Wiina.
AppId={{9C1A5E1D-7A5B-4E1F-9C3D-2B6A8F4E7D10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\Wiina
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile={#MyPublishDir}\LICENSE
OutputDir={#MyOutputDir}
OutputBaseFilename=Wiina-Setup-{#MyAppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; The app writes only to %LOCALAPPDATA%\Wiina, so a per-user install is enough.
CloseApplications=yes
RestartApplications=no
; In-place upgrade: the same AppId means Setup replaces the previous version in
; place instead of adding a second entry. Keep the previous install folder and
; the previous icon/task choices so an upgrade never leaves duplicate shortcuts.
UsePreviousAppDir=yes
UsePreviousTasks=yes
; Uninstall entry in Settings > Apps: product icon + readable name.
SetupIconFile=..\src\Wiina.App\Assets\wiina.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; The executable embeds Assets\wiina.ico; no separate icon file is installed.

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.KeepUserDataPrompt=是否保留配置文件？%n%n“是”（推荐）：保留 %1 中的设置、播放历史、插件和日志，重新安装后可以继续使用。%n“否”：同时删除这个配置文件夹（不会删除你的媒体文件；Wiina 注册的文件关联始终会被移除）。
english.KeepUserDataPrompt=Keep Wiina settings?%n%nYes (recommended): keep the settings, playback history, plugins and logs in %1 so a later install picks them up.%nNo: also delete that configuration folder (your media files are never touched; Wiina's file associations are always removed).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Same payload as the portable zip, except the portable uninstaller: the
; installed copy is removed through Inno's uninstaller instead, which also
; handles the keep/delete-settings question below.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "{#MyUninstallerExe},*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
; LICENSE/NOTICE/README.md/SOURCE.txt are expected in the publish directory.

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  // Mirrors FileAssociationCatalog.VideoExtensions + AudioExtensions
  // (src\Wiina.Core\Files\FileAssociationCatalog.cs).
  OpenWithExtensions =
    'mp4|mkv|avi|mov|wmv|flv|webm|m4v|ts|m2ts|mpg|mpeg|3gp|ogv|rmvb|' +
    'mp3|flac|aac|wav|m4a|ogg|opus|wma|ape';

  VideoProgId = 'Wiina.Video';
  AudioProgId = 'Wiina.Audio';
  // FileAssociationCatalog.CapabilitiesPath: the tree also holds
  // \FileAssociations; the tree delete removes both halves.
  CapabilitiesPath = 'Software\Wiina';
  RegisteredApplicationsKey = 'Software\RegisteredApplications';

var
  KeepUserData: Boolean;

function WiinaConfigDir: String;
begin
  Result := ExpandConstant('{localappdata}\Wiina');
end;

function WiinaHasConfig: Boolean;
begin
  Result := DirExists(WiinaConfigDir);
end;

{ Removes both Wiina ProgID markers from one extension's OpenWithProgids and
  clears a class default that points at a Wiina ProgID (that default is written
  by the app's elevated "associate as administrator" path). A default owned by
  another handler is never touched. Returns True when something was deleted. }
function CleanExtension(Hive: Integer; Ext: String): Boolean;
var
  Key: String;
  Default: String;
begin
  Result := False;
  Key := 'Software\Classes\.' + Ext;

  if RegDeleteValue(Hive, Key + '\OpenWithProgids', VideoProgId) then
    Result := True;
  if RegDeleteValue(Hive, Key + '\OpenWithProgids', AudioProgId) then
    Result := True;

  if RegQueryStringValue(Hive, Key, '', Default) then
  begin
    if (CompareText(Default, VideoProgId) = 0) or (CompareText(Default, AudioProgId) = 0) then
    begin
      if RegDeleteValue(Hive, Key, '') then
        Result := True;
    end;
  end;
end;

{ Deletes every registry item Wiina owns under Hive. Per-user keys always go;
  HKLM keys exist only after an elevated registration and are removed when the
  uninstaller has the rights (a per-user uninstall usually does not). }
procedure RemoveAssociations(Hive: Integer; var Removed: Integer);
var
  Rest, Ext: String;
  Sep: Integer;
begin
  if RegDeleteKeyIncludingSubkeys(Hive, 'Software\Classes\' + VideoProgId) then
    Removed := Removed + 1;
  if RegDeleteKeyIncludingSubkeys(Hive, 'Software\Classes\' + AudioProgId) then
    Removed := Removed + 1;

  Rest := OpenWithExtensions;
  while Length(Rest) > 0 do
  begin
    Sep := Pos('|', Rest);
    if Sep = 0 then
    begin
      Ext := Rest;
      Rest := '';
    end
    else
    begin
      Ext := Copy(Rest, 1, Sep - 1);
      Rest := Copy(Rest, Sep + 1, Length(Rest));
    end;

    if CleanExtension(Hive, Ext) then
      Removed := Removed + 1;
  end;

  if RegDeleteKeyIncludingSubkeys(Hive, CapabilitiesPath) then
    Removed := Removed + 1;
  if RegDeleteValue(Hive, RegisteredApplicationsKey, 'Wiina') then
    Removed := Removed + 1;
end;

{ Machine-wide keys can only be deleted by an elevated uninstaller. A normal
  (non-silent) uninstall re-launches itself elevated, so this only reports what
  a silent unelevated run leaves behind; the log explains it, nothing pops up. }
function MachineAssociationsRemain: Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'Software\Classes\' + VideoProgId) or
    RegKeyExists(HKLM, 'Software\Classes\' + AudioProgId) or
    RegKeyExists(HKLM, CapabilitiesPath) or
    RegValueExists(HKLM, RegisteredApplicationsKey, 'Wiina');
end;

{ Path-filtered stop of the installed copy, shared by the installer and the
  uninstaller. Inno is a 32-bit process, so the kill runs through PowerShell
  rather than taskkill (which can only filter by image name), and the path
  filter keeps a portable/dev build running elsewhere untouched. }
function InstalledWiinaStopCommand: String;
begin
  Result :=
    'Get-Process -Name Wiina -ErrorAction SilentlyContinue | ' +
    'Where-Object { $_.Path -eq ''' + ExpandConstant('{app}\{#MyAppExeName}') + ''' } | ' +
    'Stop-Process -Force -ErrorAction SilentlyContinue';
end;

procedure RunStopCommand(Command: String);
var
  ResultCode: Integer;
begin
  Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "' + Command + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure StopInstalledWiina;
begin
  RunStopCommand(InstalledWiinaStopCommand);
  { Give the stopped process a moment to release its file handles, then retry
    once for a process that was still starting up during the first pass. }
  Sleep(500);
  RunStopCommand(InstalledWiinaStopCommand);
end;

// In-place upgrade: a running Wiina keeps its own files locked and the previous
// version has to be removed before the new files are copied, so close it here.
// Inno's Restart Manager (CloseApplications=yes) also asks apps that hold files
// in the app directory to close gracefully; this explicit stop covers the case
// where the old window does not answer (mpv fullscreen etc.).
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  NeedsRestart := False;
  StopInstalledWiina;
end;

function InitializeUninstall: Boolean;
var
  Answer, ErrorCode: Integer;
begin
  Result := True;
  KeepUserData := True;

  { The uninstaller raises its own UAC prompt once, at launch, instead of
    spawning an elevated PowerShell helper later. The elevated instance runs
    the complete uninstall, so this instance only hands over and exits. If the
    user declines UAC the uninstall still proceeds unelevated: HKCU and the app
    folder are removed, and the log records any HKLM keys that need an
    administrator later. Silent runs never prompt (a UAC dialog nobody can
    answer would just hang), so they keep the unelevated behaviour. }
  if (not IsAdmin) and (not UninstallSilent) then
  begin
    if ShellExec(
         'runas', ExpandConstant('{uninstallexe}'), '',
         ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    begin
      Result := False;
      Exit;
    end;

    Log('uninstaller: elevation was declined; continuing without machine-wide cleanup');
  end;

  { Silent uninstall and installs without a config folder always keep the
    settings, exactly like before. }
  if UninstallSilent or (not WiinaHasConfig) then
  begin
    Exit;
  end;

  Answer :=
    MsgBox(
      FmtMessage(ExpandConstant('{cm:KeepUserDataPrompt}'), [WiinaConfigDir]),
      mbConfirmation,
      MB_YESNO);
  { Only an explicit "No" deletes the settings: closing the prompt (or any
    other answer) keeps them and the uninstall still proceeds. }
  KeepUserData := Answer <> IDNO;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RemovedUser, RemovedMachine: Integer;
begin
  if CurUninstallStep <> usUninstall then
  begin
    Exit;
  end;

  { The file list cannot be removed while the installed Wiina holds its
    executable open; the path-filtered stop mirrors the installer so a
    portable copy running elsewhere stays up. }
  StopInstalledWiina;

  { Associations are always removed, whether or not the config is kept. The
    normal interactive path is elevated, so the machine-wide copies go too; a
    silent unelevated run cleans HKCU and logs what is left for a later
    administrator run - it never shows a leftover warning. }
  RemovedUser := 0;
  RemovedMachine := 0;
  RemoveAssociations(HKCU, RemovedUser);
  if IsAdmin then
  begin
    RemoveAssociations(HKLM, RemovedMachine);
  end
  else if MachineAssociationsRemain then
  begin
    Log('associations: machine-wide keys remain; run the uninstaller as administrator to remove them');
  end;
  Log('associations: ' + IntToStr(RemovedUser) + ' per-user and ' +
    IntToStr(RemovedMachine) + ' machine-wide item(s) removed');

  if not KeepUserData then
  begin
    DelTree(WiinaConfigDir, True, True, True);
  end;
end;
