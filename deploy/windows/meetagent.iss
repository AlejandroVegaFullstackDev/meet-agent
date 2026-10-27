; Instalador de MeetAgent para Windows (Inno Setup 6). Sin administrador: se instala para el usuario actual.
; Lo compila el CI: iscc /DAppVersion=<v> deploy\windows\meetagent.iss  (necesita dist\win-x64 de scripts/publish.sh
; y los manifests de scripts/host-manifests.sh dist/win-x64 meetagent-host.exe).
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8F0C2E55-6B3A-4C1E-9E7B-2D7E3A5C9F11}
AppName=MeetAgent
AppVersion={#AppVersion}
AppVerName=MeetAgent {#AppVersion}
AppPublisher=Alejandro Vega
AppPublisherURL=https://github.com/AlejandroVegaFullstackDev/meet-agent
AppSupportURL=https://github.com/AlejandroVegaFullstackDev/meet-agent/issues
DefaultDirName={localappdata}\Programs\MeetAgent
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist\release
OutputBaseFilename=MeetAgent-Setup-{#AppVersion}
SetupIconFile=..\..\app\src\MeetAgent.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\MeetAgent.exe
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "startup"; Description: "Abrir MeetAgent al iniciar Windows (queda en la bandeja)"; Flags: unchecked

[Files]
Source: "..\..\dist\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\..\extension\*"; DestDir: "{app}\extension"; Excludes: "test,README.md"; Flags: recursesubdirs ignoreversion
Source: "..\..\deploy\release\LEEME.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\docs\mcp.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MeetAgent"; Filename: "{app}\MeetAgent.exe"
Name: "{autodesktop}\MeetAgent"; Filename: "{app}\MeetAgent.exe"; Tasks: desktopicon

[Registry]
; Native messaging: la extensión de Meet habla con meetagent-host (los manifests usan ruta relativa).
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.meetagent.host"; ValueType: string; ValueName: ""; ValueData: "{app}\com.meetagent.host.chromium.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.meetagent.host"; ValueType: string; ValueName: ""; ValueData: "{app}\com.meetagent.host.chromium.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.meetagent.host"; ValueType: string; ValueName: ""; ValueData: "{app}\com.meetagent.host.chromium.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Mozilla\NativeMessagingHosts\com.meetagent.host"; ValueType: string; ValueName: ""; ValueData: "{app}\com.meetagent.host.firefox.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MeetAgent"; ValueData: """{app}\MeetAgent.exe"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MeetAgent.exe"; Description: "{cm:LaunchProgram,MeetAgent}"; Flags: nowait postinstall skipifsilent

; Las reuniones (%LOCALAPPDATA%\MeetAgent\data) no se borran al desinstalar: son del usuario.
