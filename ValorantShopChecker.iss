#ifndef AppVersion
  #error Build with Build.ps1 to supply the version.
#endif
[Setup]
AppId={{72618435-9E87-413F-9454-58CF8C159027}
AppName=Valorant Shop Checker
AppVersion={#AppVersion}
AppPublisher=aidenculpepper
AppPublisherURL=https://github.com/aidenculpepper/ValorantShopChecker
DefaultDirName={localappdata}\Programs\ValorantShopChecker
DisableDirPage=yes
DefaultGroupName=Valorant Shop Checker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir=.
OutputBaseFilename=ValorantShopCheckerSetup
SetupIconFile=Nightshift.ico
UninstallDisplayIcon={app}\ValorantShopChecker.exe
UninstallDisplayName=Valorant Shop Checker
VersionInfoVersion={#AppVersion}.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dark
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ValorantShopChecker.Foundation
[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "ValorantShopChecker.exe"; Flags: dontcopy
Source: "ValorantShopChecker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Nightshift.ico"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\Valorant Shop Checker"; Filename: "{app}\ValorantShopChecker.exe"; IconFilename: "{app}\Nightshift.ico"
Name: "{userdesktop}\Valorant Shop Checker"; Filename: "{app}\ValorantShopChecker.exe"; IconFilename: "{app}\Nightshift.ico"; Tasks: desktopicon
[Run]
Filename: "{app}\ValorantShopChecker.exe"; Description: "Open Valorant Shop Checker"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\ValorantShopChecker.exe"; Flags: nowait runasoriginaluser; Check: IsUpdate
[Code]
function IsUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:SHOPUPDATE|0}') = '1';
end;
function InitializeSetup(): Boolean;
var Code: Integer; Arguments: String;
begin
  Result := True;
  if IsUpdate() then exit;
  ExtractTemporaryFile('ValorantShopChecker.exe');
  Arguments := '--install-latest';
  if WizardSilent() then Arguments := Arguments + ' --silent-update';
  if Exec(ExpandConstant('{tmp}\ValorantShopChecker.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
    if Code = 2 then Result := False
    else if Code <> 0 then begin
      if WizardSilent() then Result := False
      else Result := MsgBox('Could not check for a newer installer. Install bundled version {#AppVersion} anyway?', mbConfirmation, MB_YESNO) = IDYES;
    end;
  end else begin
    if WizardSilent() then Result := False
    else Result := MsgBox('Could not start the update check. Install bundled version {#AppVersion} anyway?', mbConfirmation, MB_YESNO) = IDYES;
  end;
end;
