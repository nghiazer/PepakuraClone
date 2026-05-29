; 4H-Unfolder — Inno Setup 6 installer script
; Build from project root:  powershell installer\build-installer.ps1 -Version 0.0.3.H
; Or directly:              iscc /DMyAppVersion=0.0.3.H installer\4H-Unfolder.iss
; Requires: Inno Setup 6.x  (winget install JRSoftware.InnoSetup)

#ifndef MyAppVersion
  #define MyAppVersion "0.0.3.H"
#endif

#define MyAppName      "4H-Unfolder"
#define MyAppPublisher "NghiaZer"
#define MyAppURL       "https://github.com/NghiaZer/4H-Unfolder"
#define MyAppExeName   "4H-Unfolder.exe"
#define MyAppId        "{938DFECB-81FE-4DA4-BD65-68E8B459FBD0}"
#define SourceDir      "..\publish\v" + MyAppVersion

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\publish
OutputBaseFilename=4H-Unfolder-v{#MyAppVersion}-setup
SetupIconFile=..\src\FourHUnfolder.App\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll";           DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";                        Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName,'&','&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Uncomment to wipe user settings on uninstall:
; Type: filesandordirs; Name: "{localappdata}\4H-Unfolder"
