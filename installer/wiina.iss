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
; %LOCALAPPDATA%\Wiina and HKCU, so no elevation is needed and no machine-wide
; state is touched. Wiina's file associations are opt-in from the app settings;
; the uninstaller removes them only when the user asks for a full data wipe.
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
#define MyAppVersion "1.2.4"
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
; Uninstall entry in Settings > Apps: product icon + readable name.
SetupIconFile=..\src\Wiina.App\Assets\wiina.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; The executable embeds Assets\wiina.ico; no separate icon file is installed.

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.KeepUserDataPrompt=是否保留配置文件？%n%n“是”（推荐）：保留 %1 中的配置、日志，以及 Wiina 注册的文件关联。%n“否”：同时删除这个配置文件夹和 Wiina 的注册表项（永远不会删除你的媒体文件）。
english.KeepUserDataPrompt=Keep Wiina settings?%n%nYes (recommended): keep the configuration, logs and file associations in %1.%nNo: also delete that configuration folder and Wiina's registry entries (your media files are never touched).

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
  // (src\Wiina.Core\Files\FileAssociationCatalog.cs). Used to drop the
  // Wiina.* values from each extension's OpenWithProgids key.
  OpenWithExtensions =
    'mp4|mkv|avi|mov|wmv|flv|webm|m4v|ts|m2ts|mpg|mpeg|3gp|ogv|rmvb|' +
    'mp3|flac|aac|wav|m4a|ogg|opus|wma|ape';

  VideoProgId = 'Wiina.Video';
  AudioProgId = 'Wiina.Audio';

var
  KeepUserData: Boolean;

function WiinaConfigDir: String;
begin
  Result := ExpandConstant('{localappdata}\Wiina');
end;

function WiinaHasUserData: Boolean;
begin
  Result :=
    DirExists(WiinaConfigDir) or
    RegKeyExists(HKCU, 'Software\Wiina') or
    RegKeyExists(HKCU, 'Software\Classes\' + VideoProgId) or
    RegKeyExists(HKCU, 'Software\Classes\' + AudioProgId);
end;

{ Removes the Wiina.Video/Wiina.Audio markers from every extension's
  HKCU\Software\Classes\.<ext>\OpenWithProgids. The empty keys are left to
  Windows/Explorer, which treats them as harmless, exactly like the in-app
  "unregister" path does. }
procedure RemoveOpenWithValues;
var
  Rest, Ext, Key: String;
  Sep: Integer;
begin
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

    Key := 'Software\Classes\.' + Ext + '\OpenWithProgids';
    RegDeleteValue(HKCU, Key, VideoProgId);
    RegDeleteValue(HKCU, Key, AudioProgId);
  end;
end;

{ Deletes only Wiina's own configuration and registry state. No user media is
  ever referenced here. }
procedure DeleteUserData;
begin
  RemoveOpenWithValues;
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + VideoProgId);
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + AudioProgId);
  RegDeleteValue(HKCU, 'Software\RegisteredApplications', 'Wiina');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Wiina');
  DelTree(WiinaConfigDir, True, True, True);
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  { Silent uninstall and installs without user data always keep settings. }
  KeepUserData := True;
  if (not UninstallSilent) and WiinaHasUserData then
  begin
    KeepUserData :=
      MsgBox(
        FmtMessage(ExpandConstant('{cm:KeepUserDataPrompt}'), [WiinaConfigDir]),
        mbConfirmation,
        MB_YESNO) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    if not KeepUserData then
      DeleteUserData;
  end;
end;
