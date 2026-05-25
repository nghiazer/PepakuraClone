; 4H-Unfolder — Inno Setup 6 installer script
; Compile with: iscc 4H-Unfolder.iss
; Requires: Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
; Expected layout when compiling:
;   installer\
;     4H-Unfolder.iss           <- this file
;     dist\
;       4H-Unfolder.exe
;       wpfgfx_cor3.dll
;       PresentationNative_cor3.dll
;       D3DCompiler_47_cor3.dll
;       PenImc_cor3.dll
;       vcruntime140_cor3.dll
;       assimp.dll
;
; To populate dist\, run:  scripts\prepare-installer.ps1

#define MyAppName      "4H-Unfolder"
#define MyAppVersion   "0.0.3.C"
#define MyAppPublisher "NghiaZer"
#define MyAppURL       "https://github.com/NghiaZer/4H-Unfolder"
#define MyAppExeName   "4H-Unfolder.exe"
#define MyAppId        "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=4H-Unfolder-v{#MyAppVersion}-Setup
SetupIconFile=..\src\FourHUnfolder.App\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\{#MyAppExeName}";             DestDir: "{app}"; Flags: ignoreversion
Source: "dist\wpfgfx_cor3.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "dist\PresentationNative_cor3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\D3DCompiler_47_cor3.dll";     DestDir: "{app}"; Flags: ignoreversion
Source: "dist\PenImc_cor3.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "dist\vcruntime140_cor3.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "dist\assimp.dll";                 DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName,'&','&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Uncomment to wipe user settings on uninstall:
; Type: filesandordirs; Name: "{localappdata}\4H-Unfolder"
