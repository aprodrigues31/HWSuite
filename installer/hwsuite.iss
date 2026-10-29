; HWSuite installer (Inno Setup 6). Built by CI:
;   ISCC.exe /DAppVersion=0.14.0 /DGuiDir=..\gui /DCliExe=..\release\hwsuite.exe /O..\artifacts installer\hwsuite.iss
; Per-user install (no administrator prompt): %LOCALAPPDATA%\Programs\HWSuite, Start menu entry,
; optional desktop icon, uninstaller in "Add or remove programs".

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef GuiDir
  #define GuiDir "..\gui"
#endif
#ifndef CliExe
  #define CliExe "..\release\hwsuite.exe"
#endif

[Setup]
AppId={{6F1C2B8E-4D3A-4F7B-9C1E-2A5D8B0E7F31}
AppName=HWSuite
AppVersion={#AppVersion}
AppVerName=HWSuite {#AppVersion}
AppPublisher=HWSuite contributors
AppPublisherURL=https://github.com/aprodrigues31/HWSuite
AppSupportURL=https://github.com/aprodrigues31/HWSuite/issues
AppUpdatesURL=https://github.com/aprodrigues31/HWSuite/releases
DefaultDirName={localappdata}\Programs\HWSuite
DefaultGroupName=HWSuite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=hwsuite-setup-{#AppVersion}
SetupIconFile=..\src\HWSuite.Gui\Assets\HWSuite.ico
UninstallDisplayIcon={app}\HWSuite.Gui.exe
UninstallDisplayName=HWSuite {#AppVersion}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#GuiDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#CliExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\HWSuite"; Filename: "{app}\HWSuite.Gui.exe"
Name: "{autodesktop}\HWSuite"; Filename: "{app}\HWSuite.Gui.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HWSuite.Gui.exe"; Description: "{cm:LaunchProgram,HWSuite}"; Flags: nowait postinstall skipifsilent
; In-app update: the app runs the setup with /SILENT /RELAUNCH=1 and exits; the new version starts here.
Filename: "{app}\HWSuite.Gui.exe"; Flags: nowait; Check: Relaunch

[UninstallRun]
; The "start with Windows" scheduled task points at this folder; remove it with the app (ignored if absent).
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""HWSuite"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
function Relaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
