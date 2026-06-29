#define AppName "NixMaster"
#define AppVersion "1.0.1"
#define AppPublisher "NixMaster Corp"
#define AppExeName "NixMaster.exe"
#define AppIconPath "NixMaster\app_icon.ico"
#define PublishPath "NixMaster\Publish"

[Setup]
AppId={{C6D2D49D-42A9-4E60-8488-84EE7546BA8B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=.\InstallerOutput
OutputBaseFilename=NixMaster_Setup_v{#AppVersion}
SetupIconFile={#AppIconPath}
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishPath}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\app_icon.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; IconFilename: "{app}\app_icon.ico"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
