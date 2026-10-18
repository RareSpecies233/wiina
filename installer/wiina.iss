; Wiina v1.0.0 — optional Inno Setup 6 script.
;
; NOT built or shipped by this repository. The official v1 distribution is the
; portable artifacts\wiina-1.0.0-win-x64.zip produced by scripts\package.ps1.
;
; To build locally:
;   1. Publish first (scripts\package.ps1 or the dotnet publish command below).
;   2. Install Inno Setup 6 and open this file.
;   3. Compile; output goes to artifacts\.
;
; It intentionally ships the same payload as the portable zip and nothing else.

#define MyAppName "Wiina"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Wiina contributors"
#define MyAppUrl "https://github.com/RareSpecies233/wiina"
#define MyAppExeName "Wiina.exe"
; Publish output produced by scripts\package.ps1 or:
;   dotnet publish src\Wiina.App\Wiina.App.csproj -c Release -r win-x64 --self-contained true ^
;     -o artifacts\publish -p:Version=1.0.0 -p:DebugType=None -p:DebugSymbols=false
#define MyPublishDir "..\artifacts\publish"

[Setup]
AppId={{9C1A5E1D-7A5B-4E1F-9C3D-2B6A8F4E7D10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\Wiina
DefaultGroupName=Wiina
DisableProgramGroupPage=yes
LicenseFile={#MyPublishDir}\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=Wiina-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; The app writes only to %LOCALAPPDATA%\Wiina, so a per-user install is enough.
UninstallDisplayIcon={app}\{#MyAppExeName}
; No custom .ico is shipped; the executable's embedded icon is used.

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; LICENSE/NOTICE/SOURCE.txt are expected in the publish directory; the uninstall
; entry below keeps them visible in the install folder.

[Icons]
Name: "{autoprograms}\Wiina"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Wiina"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Wiina}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
