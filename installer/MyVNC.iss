; Inno Setup script for MyVNC. MyAppVersion is passed in from build-installer.ps1 via
; /DMyAppVersion=x.y.z (read from the root VERSION file there), so a release bump (see
; AGENTS.md) doesn't require touching this script. The fallback below only applies if this file
; is ever compiled directly without going through build-installer.ps1.
;
; AppId is a fixed GUID: Inno Setup uses it to identify "the same app" across versions for
; upgrade/uninstall tracking. Never change it once an installer has shipped.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "MyVNC"
#define MyAppPublisher "MyVNC"
#define MyAppExeName "MyVNC.App.exe"
#define PublishDir "publish"

[Setup]
AppId={{DA96DB86-21FA-4CAD-87D7-05CA231360CC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user, no-admin install: friends receiving this don't need admin rights on their own
; machine, and there's no UAC prompt in the way.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=MyVNC-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\MyVNC.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
