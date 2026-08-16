#define MyAppName "ELKA Power Throttle Control"
#define MyAppPublisher "ElkaSoft"
#define MyAppExeName "ELKA.PowerThrottleControl.exe"
#define MyCompanyFolderName "Elka Software"
#define MyInstallFolderName "ELKA Power Throttle Control"

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef SourcePublishDir
  #error SourcePublishDir not defined. Pass /DSourcePublishDir=...
#endif

#ifndef InstallerOutputDir
  #error InstallerOutputDir not defined. Pass /DInstallerOutputDir=...
#endif

[Setup]
AppId={{A3D6B7E9-4A78-4C46-91D7-8EF52F76D2F1}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/torment78/ELKA.PowerThrottleControl
AppSupportURL=https://github.com/torment78/ELKA.PowerThrottleControl/issues
AppUpdatesURL=https://github.com/torment78/ELKA.PowerThrottleControl/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}

DefaultDirName={autopf}\{#MyCompanyFolderName}\{#MyInstallFolderName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousLanguage=yes

OutputDir={#InstallerOutputDir}
OutputBaseFilename=ELKA_Power_Throttle_Control_Setup_{#AppVersion}
SetupIconFile=..\ELKA.PowerThrottleControl\Assets\ELKA.PowerThrottleControl.ico
WizardImageFile=Branding\wizard-left.png
WizardSmallImageFile=Branding\wizard-small.png
LicenseFile=..\LICENSE

WizardStyle=modern dark
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "runafter"; Description: "Launch ELKA Power Throttle Control after installation"; GroupDescription: "Post-install:"

[Files]
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ELKA Power Throttle Control"; Flags: nowait postinstall skipifsilent; Tasks: runafter

[UninstallDelete]
Type: dirifempty; Name: "{app}"
