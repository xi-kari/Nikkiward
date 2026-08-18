#define ProductName "Nikkiward"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-preview.3"
#endif

#ifndef MyVersionInfoVersion
  #define MyVersionInfoVersion "0.1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\Nikkiward\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{D5DBD5C6-0E4C-4BA8-B6D8-2B2E497E1AF3}
AppName={#ProductName}
AppVersion={#MyAppVersion}
AppVerName={#ProductName} {#MyAppVersion}
AppPublisher=xi-kari
AppPublisherURL=https://github.com/xi-kari/Nikkiward
AppSupportURL=https://github.com/xi-kari/Nikkiward/issues
DefaultDirName={localappdata}\Programs\Nikkiward
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=Nikkiward-Setup-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#PublishDir}\Assets\NikkiwardIcon.ico
UninstallDisplayIcon={app}\Nikkiward.exe
Uninstallable=yes
VersionInfoVersion={#MyVersionInfoVersion}
VersionInfoProductVersion={#MyVersionInfoVersion}
VersionInfoCompany=xi-kari
VersionInfoDescription=Infinity Nikki community launcher and local management tool
VersionInfoCopyright=Copyright (c) 2026 xi-kari

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Nikkiward"; Filename: "{app}\Nikkiward.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Nikkiward"; Filename: "{app}\Nikkiward.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Nikkiward.exe"; Description: "Launch Nikkiward"; Flags: nowait postinstall skipifsilent
