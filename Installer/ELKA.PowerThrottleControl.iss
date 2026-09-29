#define MyAppName "ELKA Power Throttle Control"
#define MyAppPublisher "ElkaSoft"
#define MyAppExeName "ELKA.PowerThrottleControl.exe"
#define MyCompanyFolderName "ElkaSoft"
#define MyInstallFolderName "ELKA Power Throttle Control"

#ifndef AppVersion
  #define AppVersion "1.3.2"
#endif

#ifndef SourcePublishDir
  #error SourcePublishDir not defined. Pass /DSourcePublishDir=...
#endif

#ifndef InstallerOutputDir
  #error InstallerOutputDir not defined. Pass /DInstallerOutputDir=...
#endif

#if Ver < EncodeVer(6, 6, 0)
  #error This installer requires Inno Setup 6.6 or newer.
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

DefaultDirName={code:GetDefaultInstallDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
UsePreviousAppDir=no
UsePreviousTasks=yes
UsePreviousLanguage=yes

OutputDir={#InstallerOutputDir}
OutputBaseFilename=ELKA_Power_Throttle_Control_Setup_{#AppVersion}
SetupIconFile=..\ELKA.PowerThrottleControl\Assets\ELKA.PowerThrottleControl.ico
WizardImageFile=Branding\wizard-left.png
WizardSmallImageFile=Branding\wizard-small.png
LicenseFile=..\LICENSE

WizardStyle=modern dark polar includetitlebar
WizardSizePercent=120,120
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
Source: "Branding\ElkaSoft.png"; Flags: dontcopy
Source: "{#SourcePublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ELKA Power Throttle Control"; Flags: nowait postinstall skipifsilent; Tasks: runafter

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
const
  AppUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A3D6B7E9-4A78-4C46-91D7-8EF52F76D2F1}_is1';

var
  WelcomeLogo: TBitmapImage;
  FinishedLogo: TBitmapImage;

function HasLegacyDefaultInstall: Boolean;
var
  PreviousDir: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, AppUninstallKey, 'InstallLocation', PreviousDir) then
    Result := SameText(RemoveBackslashUnlessRoot(PreviousDir),
      ExpandConstant('{autopf}\Elka Software\{#MyInstallFolderName}'));
end;

function GetDefaultInstallDir(Param: String): String;
var
  PreviousDir: String;
begin
  // Move the former default to ElkaSoft; remember deliberately chosen custom paths.
  Result := ExpandConstant('{autopf}\{#MyCompanyFolderName}\{#MyInstallFolderName}');
  if not HasLegacyDefaultInstall then
    if RegQueryStringValue(HKLM64, AppUninstallKey, 'InstallLocation', PreviousDir) then
      if PreviousDir <> '' then
        Result := RemoveBackslashUnlessRoot(PreviousDir);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  LegacyDir: String;
  ExitCode: Integer;
begin
  Result := '';
  if not HasLegacyDefaultInstall then
    Exit;

  LegacyDir := ExpandConstant('{autopf}\Elka Software\{#MyInstallFolderName}');
  if SameText(RemoveBackslashUnlessRoot(WizardDirValue), LegacyDir) then
    Exit;

  // Use this product's registered uninstaller so shortcuts and uninstall records
  // migrate together. It does not remove the per-user settings or Windows rules.
  if not RegQueryStringValue(HKLM64, AppUninstallKey, 'UninstallString', Uninstaller) then
  begin
    Result := 'The previous installation could not be located. Please uninstall it, then run Setup again. Your settings will be kept.';
    Exit;
  end;
  Uninstaller := RemoveQuotes(Uninstaller);
  if (not SameText(ExtractFileDir(Uninstaller), LegacyDir)) or (not FileExists(Uninstaller)) then
  begin
    Result := 'The previous uninstaller is missing or is outside the expected application folder. Please uninstall the previous version before continuing.';
    Exit;
  end;

  Log('Migrating the previous default installation to ' + WizardDirValue);
  if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', LegacyDir,
    SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Could not start the previous uninstaller. Close ELKA Power Throttle Control and try again.'
  else if ExitCode <> 0 then
    Result := 'The previous version could not be removed (code ' + IntToStr(ExitCode) + '). Close the application and try again.';
end;

procedure AddBrandLogo(var Logo: TBitmapImage; ParentPage: TNewNotebookPage; LeftEdge: Integer);
begin
  Logo := TBitmapImage.Create(WizardForm);
  Logo.Parent := ParentPage;
  Logo.SetBounds(LeftEdge, ParentPage.Height - ScaleY(120), ScaleX(108), ScaleY(108));
  Logo.Stretch := True;
  Logo.PngImage.LoadFromFile(ExpandConstant('{tmp}\ElkaSoft.png'));
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('ElkaSoft.png');

  WizardForm.WelcomeLabel1.Caption := 'ELKA Power' + #13#10 + 'Throttle Control';
  WizardForm.WelcomeLabel1.Height := ScaleY(62);
  WizardForm.WelcomeLabel2.Top := WizardForm.WelcomeLabel1.Top + WizardForm.WelcomeLabel1.Height + ScaleY(12);
  WizardForm.WelcomeLabel2.Caption :=
    'Manage Windows per-application power throttling from one focused desktop utility.' + #13#10 + #13#10 +
    'The main application runs normally and only requests administrator approval when Windows power settings are changed.' + #13#10 + #13#10 +
    'Version {#AppVersion}  |  Windows x64';

  WizardForm.FinishedHeadingLabel.Caption := 'Power control is ready';
  WizardForm.FinishedLabel.Caption :=
    'ELKA Power Throttle Control has been installed.' + #13#10 + #13#10 +
    'Select Finish to close Setup and launch the application.';

  AddBrandLogo(WelcomeLogo, WizardForm.WelcomePage, WizardForm.WelcomeLabel1.Left);
  AddBrandLogo(FinishedLogo, WizardForm.FinishedPage, WizardForm.FinishedHeadingLabel.Left);
end;
